using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>
/// Pause for everyone: on a festive or emergency day every package running then (any frequency) of Active and Paused customers is
/// paused; the end date always moves and the customer's own paused count is not changed. Later packages over the day get it too, and it
/// can be removed before the day.
/// </summary>
public class FestivePauseTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static FestivePauseService Service(TestDatabase test, FitzobitDbContext db) =>
        new(db, test.Schedule(db), test.AuditFor(db), test.User, test.Clock, test.Automation(db));

    private sealed record Seed(int Anitha, int Bala, int Chitra, int Devi, int Esther);

    /// <summary>Today is Wednesday 7 October 2026; the festive day is Wednesday 14 October.</summary>
    private static async Task<Seed> SeedAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        async Task<int> Create(string name, string phone, string frequency, DateOnly start) =>
            (await test.Commands(db).CreateAsync(test.NewCustomer(name, phone, frequency: frequency, start: start))).Id;
        async Task Status(int id, string code)
        {
            var detail = await test.Queries(db).GetDetailAsync(id);
            await test.Commands(db).UpdateAsync(id, CustomerTests.Update(detail, r => r.StatusId = test.Entry(DictionaryType.CustomerStatus, code)));
        }

        var anitha = await Create("Anitha", "9000000001", "MONTHLY", D(10, 5));   // Mon 5 Oct – Tue 3 Nov
        await test.Pauses(db).PauseAsync(anitha, new PauseCustomerRequest { Dates = [D(10, 8), D(10, 9), D(10, 10)] }); // 3 own pauses → Fri 6 Nov
        var bala = await Create("Bala", "9000000002", "WEEKLY", D(10, 12));       // Mon 12 – Sat 17 Oct (weekly cannot pause by itself)
        var chitra = await Create("Chitra", "9000000003", "WEEKLY", D(10, 12));   // will be Inactive
        var devi = await Create("Devi", "9000000004", "MONTHLY", D(10, 5));       // Paused by hand
        var esther = await Create("Esther", "9000000005", "MONTHLY", D(10, 5));   // already paused the festive day herself
        await test.Pauses(db).PauseAsync(esther, new PauseCustomerRequest { Dates = [D(10, 14)] });
        await Status(chitra, CustomerStatusCodes.Inactive);
        await Status(devi, CustomerStatusCodes.Paused);
        return new Seed(anitha, bala, chitra, devi, esther);
    }

    private static async Task<CustomerPackage> PackageAsync(TestDatabase test, int customerId)
    {
        await using var db = test.CreateContext();
        return await db.CustomerPackages.AsNoTracking().Include(p => p.Pauses).Where(p => p.CustomerId == customerId).OrderBy(p => p.Id).FirstAsync();
    }

    [Fact]
    public async Task Every_running_package_is_paused_and_its_end_date_always_moves()
    {
        using var test = new TestDatabase();
        var seed = await SeedAsync(test);
        var request = new FestivePauseRequest { Dates = [D(10, 14)], Reason = "Diwali" };

        await using (var db = test.CreateContext())
        {
            var preview = await Service(test, db).PreviewAsync(request);
            Assert.Equal((3, 3), (preview.Customers, preview.Packages)); // Anitha, Bala, Devi — not Inactive Chitra, not Esther (already paused)
        }
        Assert.Equal(D(11, 6), (await PackageAsync(test, seed.Anitha)).EndDate); // the preview saved nothing

        await using (var db = test.CreateContext())
        {
            var created = await Service(test, db).CreateAsync(request);
            Assert.Equal([seed.Anitha, seed.Bala, seed.Devi], created.Items.Select(i => i.CustomerId));
        }

        var anitha = await PackageAsync(test, seed.Anitha);
        Assert.Equal((D(11, 7), 3), (anitha.EndDate, anitha.PausedCount)); // extended; her own paused count is unchanged
        var festivePause = anitha.Pauses.Single(p => p.PausedDate == D(10, 14));
        Assert.Equal(("Diwali", true), (festivePause.Reason, festivePause.FestivePauseId != null));
        Assert.Equal((D(10, 19), 0), ((await PackageAsync(test, seed.Bala)).EndDate, (await PackageAsync(test, seed.Bala)).PausedCount)); // weekly too
        Assert.Equal(D(11, 4), (await PackageAsync(test, seed.Devi)).EndDate);
        Assert.Equal(D(10, 17), (await PackageAsync(test, seed.Chitra)).EndDate);
        Assert.Single((await PackageAsync(test, seed.Esther)).Pauses); // her own pause stays as it was

        await using var check = test.CreateContext();
        var list = Assert.Single(await Service(test, check).ListAsync());
        Assert.Equal((D(10, 14), "Diwali", 3, 3, true), (list.Date, list.Reason, list.Customers, list.Packages, list.CanRemove));
        Assert.Contains(await check.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == AuditActions.FestivePauseApplied);
    }

    [Theory]
    [InlineData(10, 18, "Sunday")]
    [InlineData(10, 6, "today or future")]
    public async Task Invalid_days_are_refused(int month, int day, string expected)
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using var db = test.CreateContext();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(test, db).CreateAsync(new FestivePauseRequest { Dates = [D(month, day)] }));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task The_same_day_cannot_be_paused_for_everyone_twice()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using (var db = test.CreateContext()) await Service(test, db).CreateAsync(new FestivePauseRequest { Dates = [D(10, 14)] });
        await using var again = test.CreateContext();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(test, again).CreateAsync(new FestivePauseRequest { Dates = [D(10, 14)] }));
        Assert.Contains("already paused for everyone", error.Message);
    }

    [Fact]
    public async Task Packages_saved_later_over_the_day_are_paused_too_and_keep_it_when_redated()
    {
        using var test = new TestDatabase();
        var seed = await SeedAsync(test);
        await using (var db = test.CreateContext()) await Service(test, db).CreateAsync(new FestivePauseRequest { Dates = [D(10, 14)], Reason = "Diwali" });

        int added;
        await using (var db = test.CreateContext())
            added = (await test.Packages(db).AddAsync(seed.Chitra, test.NewPackage("MONTHLY", D(10, 13), "OATS"))).Id; // Tue 13 Oct – Wed 11 Nov
        await using (var db = test.CreateContext())
        {
            var p = await db.CustomerPackages.AsNoTracking().Include(x => x.Pauses).SingleAsync(x => x.Id == added);
            Assert.Equal((D(11, 12), 0, 1), (p.EndDate, p.PausedCount, p.Pauses.Count));
        }

        // Bala's weekly package moves to start Tue 13: 13, 15, 16, 17, 19, 20 with the festive 14th still skipped.
        var bala = await PackageAsync(test, seed.Bala);
        await using (var db = test.CreateContext())
        {
            var saved = await test.Packages(db).UpdateAsync(bala.Id, new UpdatePackageRequest
            {
                PreferenceId = bala.PreferenceId, MealTypeId = bala.MealTypeId, Quantity = bala.Quantity, FrequencyId = bala.FrequencyId, StartDate = D(10, 13),
                ExpectedUpdatedAt = bala.UpdatedAt, ConfirmScheduleChange = true
            });
            Assert.Equal((D(10, 20), 0), (saved.EndDate, saved.PausedCount));
        }
        Assert.Single((await PackageAsync(test, seed.Bala)).Pauses, p => p.PausedDate == D(10, 14) && p.FestivePauseId != null);
    }

    [Fact]
    public async Task Removing_before_the_day_moves_the_end_dates_back()
    {
        using var test = new TestDatabase();
        var seed = await SeedAsync(test);
        await using (var db = test.CreateContext()) await Service(test, db).CreateAsync(new FestivePauseRequest { Dates = [D(10, 14), D(10, 7)] });

        await using (var db = test.CreateContext())
        {
            var list = await Service(test, db).ListAsync();
            var today = list.Single(f => f.Date == D(10, 7));
            Assert.False(today.CanRemove);
            Assert.Contains("already come", (await Assert.ThrowsAsync<BusinessRuleException>(() => Service(test, db).RemoveAsync(today.Id))).Message);
            await Service(test, db).RemoveAsync(list.Single(f => f.Date == D(10, 14)).Id);
        }

        Assert.Equal(D(11, 7), (await PackageAsync(test, seed.Anitha)).EndDate); // only the 7 Oct pause remains (Fri 6 → Sat 7 Nov)
        Assert.Equal(D(10, 17), (await PackageAsync(test, seed.Bala)).EndDate);  // Bala starts on the 12th: only the 14th applied, now gone
        Assert.DoesNotContain((await PackageAsync(test, seed.Bala)).Pauses, p => p.PausedDate == D(10, 14));
        await using var check = test.CreateContext();
        Assert.Equal([D(10, 7)], await check.FestivePauses.Select(f => f.Date).ToListAsync());
    }
}
