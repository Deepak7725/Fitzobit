using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>
/// The daily 12 pm run (India time): finished packages move to the past packages, customers without a package become Inactive,
/// a package starting on the next delivery day makes the customer Active, and fully paused days switch Active → Paused → Active.
/// </summary>
public class CustomerAutomationTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    /// <summary>Sets the clock to the given India time (UTC + 5:30).</summary>
    private static void At(TestDatabase test, int month, int day, int hour, int minute = 0) =>
        test.Clock.UtcNow = new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc).AddMinutes(-330);

    private static async Task<int> CreateAsync(TestDatabase test, string phone = "9876543210", string frequency = "WEEKLY", DateOnly? start = null)
    {
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(test.NewCustomer($"Customer {phone[^3..]}", phone, frequency: frequency, start: start))).Id;
    }

    private static async Task<IReadOnlyList<AutomationRunResult>> RunAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        return await test.Automation(db).RunDueAsync();
    }

    private static async Task<string> StatusAsync(TestDatabase test, int id)
    {
        await using var db = test.CreateContext();
        return await db.Customers.Where(c => c.Id == id).Select(c => c.Status!.Code).SingleAsync();
    }

    private static async Task SetStatusAsync(TestDatabase test, int id, string code)
    {
        await using var db = test.CreateContext();
        var detail = await test.Queries(db).GetDetailAsync(id);
        await test.Commands(db).UpdateAsync(id, CustomerTests.Update(detail, r => r.StatusId = test.Entry(DictionaryType.CustomerStatus, code)));
    }

    private static async Task PauseAsync(TestDatabase test, int id, params DateOnly[] dates)
    {
        await using var db = test.CreateContext();
        await test.Pauses(db).PauseAsync(id, new PauseCustomerRequest { Dates = [.. dates] });
    }

    [Fact]
    public async Task The_run_is_due_from_12_pm_and_each_day_runs_once()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var automation = test.Automation(db);

        At(test, 10, 13, 11, 59);
        Assert.Equal(D(10, 12), automation.DueDay());
        At(test, 10, 13, 12);
        Assert.Equal(D(10, 13), automation.DueDay());

        Assert.Equal([D(10, 13)], (await RunAsync(test)).Select(r => r.Day)); // the very first run only runs the due day
        Assert.Empty(await RunAsync(test));
        At(test, 10, 16, 12, 30);
        Assert.Equal([D(10, 14), D(10, 15), D(10, 16)], (await RunAsync(test)).Select(r => r.Day)); // missed days catch up in order
        Assert.Equal("2026-10-16", (await db.AppSettings.SingleAsync(s => s.Key == CustomerAutomationService.LastRunKey)).Value);
    }

    [Fact]
    public async Task A_finished_package_moves_to_the_past_and_the_customer_becomes_inactive()
    {
        using var test = new TestDatabase();
        var id = await CreateAsync(test); // weekly Mon 12 – Sat 17 Oct

        At(test, 10, 17, 11);
        await RunAsync(test); // runs Fri 16: nothing has finished yet
        Assert.Equal(CustomerStatusCodes.Active, await StatusAsync(test, id));

        At(test, 10, 17, 12);
        var run = Assert.Single(await RunAsync(test));
        Assert.Equal((1, 1, 0, 0), (run.PackagesMovedToPast, run.MadeInactive, run.MadeActive, run.MadePaused));
        Assert.Equal(CustomerStatusCodes.Inactive, await StatusAsync(test, id));

        await using var db = test.CreateContext();
        var detail = await test.Queries(db).GetDetailAsync(id);
        Assert.Empty(detail.Packages);
        var past = Assert.Single(detail.PastPackages);
        Assert.False(past.CanDelete);
        var item = Assert.Single((await test.Queries(db).ListAsync(new CustomerFilter(), 1, 25)).Items);
        Assert.Equal((0, (DateOnly?)null), (item.Packages.Count, item.EndDate));
        Assert.Equal(0, (await test.Queries(db).ListAsync(new CustomerFilter { Incomplete = true }, 1, 25)).Total); // had a package: not incomplete
        Assert.Contains(await db.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == AuditActions.CustomerPackageEnded);
        Assert.Contains("Active → Inactive", (await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.CustomerStatusChangedAutomatically)).Summary);

        var p = await db.CustomerPackages.AsNoTracking().SingleAsync();
        var edit = new UpdatePackageRequest
        {
            PreferenceId = p.PreferenceId, MealTypeId = p.MealTypeId, Quantity = 2, FrequencyId = p.FrequencyId, StartDate = p.StartDate, ExpectedUpdatedAt = p.UpdatedAt
        };
        Assert.Contains("past packages", (await Assert.ThrowsAsync<BusinessRuleException>(() => test.Packages(db).UpdateAsync(p.Id, edit))).Message);
    }

    [Fact]
    public async Task A_customer_with_another_package_running_stays_active()
    {
        using var test = new TestDatabase();
        var id = await CreateAsync(test);
        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 12), "OATS"));

        At(test, 10, 17, 12);
        var run = Assert.Single(await RunAsync(test));
        Assert.Equal((1, 0), (run.PackagesMovedToPast, run.MadeInactive));
        Assert.Equal(CustomerStatusCodes.Active, await StatusAsync(test, id));
    }

    [Fact]
    public async Task Inactive_and_paused_customers_become_active_the_day_before_a_package_starts()
    {
        using var test = new TestDatabase();
        var inactive = await CreateAsync(test, "9000000001", start: D(10, 12)); // Monday
        var paused = await CreateAsync(test, "9000000002", start: D(10, 12));
        var later = await CreateAsync(test, "9000000003", start: D(10, 13)); // Tuesday
        await SetStatusAsync(test, inactive, CustomerStatusCodes.Inactive);
        await SetStatusAsync(test, paused, CustomerStatusCodes.Paused);
        await SetStatusAsync(test, later, CustomerStatusCodes.Inactive);

        At(test, 10, 10, 12); // Saturday 12 pm looks at Monday
        Assert.Equal(2, Assert.Single(await RunAsync(test)).MadeActive);
        Assert.Equal([CustomerStatusCodes.Active, CustomerStatusCodes.Active, CustomerStatusCodes.Inactive],
            [await StatusAsync(test, inactive), await StatusAsync(test, paused), await StatusAsync(test, later)]);

        At(test, 10, 12, 12); // Monday 12 pm looks at Tuesday (the Sunday run changed nothing)
        await RunAsync(test);
        Assert.Equal(CustomerStatusCodes.Active, await StatusAsync(test, later));
    }

    [Fact]
    public async Task Fully_paused_days_switch_to_paused_the_day_before_and_back_to_active_on_the_last_one()
    {
        using var test = new TestDatabase();
        var id = await CreateAsync(test, frequency: "MONTHLY", start: D(10, 12)); // Mon 12 Oct – Tue 10 Nov
        await PauseAsync(test, id, D(10, 14), D(10, 15), D(10, 17), D(10, 19)); // Wed, Thu, Sat and (after Sunday) Mon

        var expected = new (int Day, string Status)[]
        {
            (13, CustomerStatusCodes.Paused), // Tue: Wed is paused
            (14, CustomerStatusCodes.Paused),
            (15, CustomerStatusCodes.Active), // Thu, last paused day: Fri is delivered
            (16, CustomerStatusCodes.Paused), // Fri: Sat is paused
            (17, CustomerStatusCodes.Paused), // Sat looks at Mon, also paused
            (18, CustomerStatusCodes.Paused), // the Sunday run keeps it paused
            (19, CustomerStatusCodes.Active)  // Mon: Tue is delivered
        };
        foreach (var (day, status) in expected)
        {
            At(test, 10, day, 12, 5);
            await RunAsync(test);
            Assert.True(status == await StatusAsync(test, id), $"After the 12 pm run of {day} Oct the status should be {status}.");
        }
    }

    [Fact]
    public async Task A_day_with_another_package_still_delivering_keeps_the_customer_active()
    {
        using var test = new TestDatabase();
        var id = await CreateAsync(test, frequency: "MONTHLY", start: D(10, 12));
        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("WEEKLY", D(10, 12), "OATS"));
        await PauseAsync(test, id, D(10, 14)); // only the monthly package pauses

        At(test, 10, 13, 12);
        await RunAsync(test);
        Assert.Equal(CustomerStatusCodes.Active, await StatusAsync(test, id));
    }

    [Fact]
    public async Task After_12_pm_a_pause_or_a_new_package_applies_the_rules_at_once()
    {
        using var test = new TestDatabase();
        var paused = await CreateAsync(test, "9000000001", frequency: "MONTHLY", start: D(10, 12));
        var starting = await CreateAsync(test, "9000000002", start: D(10, 12));
        await SetStatusAsync(test, starting, CustomerStatusCodes.Inactive);
        At(test, 10, 13, 12);
        await RunAsync(test);

        At(test, 10, 13, 10); // before 12 pm (the next day's run would handle it): no change yet
        await PauseAsync(test, paused, D(10, 15));
        At(test, 10, 14, 15); // 3 pm: today's run is done, so pausing tomorrow switches at once
        await PauseAsync(test, paused, D(10, 16));
        Assert.Equal(CustomerStatusCodes.Paused, await StatusAsync(test, paused));

        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(starting, test.NewPackage("WEEKLY", D(10, 15), "OATS"));
        Assert.Equal(CustomerStatusCodes.Active, await StatusAsync(test, starting)); // the new package starts tomorrow
    }

    [Fact]
    public async Task A_pause_for_today_saved_in_the_morning_switches_to_paused_at_once()
    {
        using var test = new TestDatabase();
        var id = await CreateAsync(test, frequency: "MONTHLY", start: D(10, 12));
        At(test, 10, 13, 12);
        await RunAsync(test);

        At(test, 10, 14, 2, 30); // Wednesday 2:30 am: today's delivery is paused after Tuesday's run
        await PauseAsync(test, id, D(10, 14));
        Assert.Equal(CustomerStatusCodes.Paused, await StatusAsync(test, id));

        At(test, 10, 14, 9); // a pause for tomorrow saved in the morning waits for today's 12 pm run
        await PauseAsync(test, id, D(10, 16));
        At(test, 10, 14, 12);
        await RunAsync(test); // Thursday is delivered: Active again
        Assert.Equal(CustomerStatusCodes.Active, await StatusAsync(test, id));
        At(test, 10, 15, 12);
        await RunAsync(test); // Friday 16 is paused
        Assert.Equal(CustomerStatusCodes.Paused, await StatusAsync(test, id));
    }

    [Fact]
    public async Task A_status_set_by_hand_is_left_alone_when_no_rule_applies()
    {
        using var test = new TestDatabase();
        var id = await CreateAsync(test, frequency: "MONTHLY", start: D(10, 12));
        await SetStatusAsync(test, id, CustomerStatusCodes.Paused); // paused by hand, no paused days

        At(test, 10, 14, 12);
        await RunAsync(test);
        At(test, 10, 16, 12);
        await RunAsync(test);
        Assert.Equal(CustomerStatusCodes.Paused, await StatusAsync(test, id));
    }
}
