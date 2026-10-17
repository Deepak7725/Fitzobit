using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>
/// Pausing a delivery day pauses every package running that day: each meal moves to the next delivery day (never a Sunday);
/// at most 3 pauses per package, and the day is blocked if any running package has none left.
/// </summary>
public class CustomerPauseTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    /// <summary>A customer with a weekly package Mon 12 – Sat 17 October 2026 (today is Wednesday 7 October).</summary>
    private static async Task<int> WeeklyCustomerAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(test.NewCustomer())).Id;
    }

    private static async Task<PauseResultDto> PauseAsync(TestDatabase test, int id, DateOnly date, string? reason = null)
    {
        await using var db = test.CreateContext();
        return await test.Pauses(db).PauseAsync(id, new PauseCustomerRequest { Date = date, Reason = reason });
    }

    private static async Task<List<CustomerPackage>> PackagesAsync(TestDatabase test, int id)
    {
        await using var db = test.CreateContext();
        return await db.CustomerPackages.AsNoTracking().Include(p => p.Pauses).Where(p => p.CustomerId == id).OrderBy(p => p.StartDate).ThenBy(p => p.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task Skipping_saturday_moves_the_meal_to_monday()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);

        var result = await PauseAsync(test, id, D(10, 17), "Going out of town");

        var paused = Assert.Single(result.Packages);
        Assert.Equal((D(10, 17), D(10, 19), 1, 2), (paused.PreviousEndDate, paused.NewEndDate, paused.PausedCount, paused.PausesLeft));
        var package = Assert.Single(await PackagesAsync(test, id));
        Assert.Equal((D(10, 12), D(10, 19), 1), (package.StartDate, package.EndDate, package.PausedCount));
        var pause = Assert.Single(package.Pauses);
        Assert.Equal((D(10, 17), "Going out of town", "test-user"), (pause.PausedDate, pause.Reason, pause.CreatedBy));

        await using var db = test.CreateContext();
        Assert.Equal(AuditActions.CustomerPaused, (await db.AuditLogs.OrderBy(a => a.Id).LastAsync()).Action);
        var detail = (await test.Queries(db).GetDetailAsync(id)).Packages.Single();
        Assert.Equal((6, 1, 3), (detail.DeliveryDaysLeft, detail.PausedCount, detail.MaxPauses)); // still 6 meals to deliver
    }

    [Fact]
    public async Task A_pause_applies_to_every_package_running_that_day()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        await using (var db = test.CreateContext())
        {
            await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 12), "OATS"));   // 12 Oct – 10 Nov
            await test.Packages(db).AddAsync(id, test.NewPackage("WEEKLY", D(10, 19), "LARGE"));   // starts after the paused day
        }

        var result = await PauseAsync(test, id, D(10, 14));

        Assert.Equal([(D(10, 17), D(10, 19)), (D(11, 10), D(11, 11))], result.Packages.Select(p => (p.PreviousEndDate, p.NewEndDate)));
        var packages = await PackagesAsync(test, id);
        Assert.Equal([1, 1, 0], packages.Select(p => p.PausedCount));
        Assert.Equal([D(10, 19), D(11, 11), D(10, 24)], packages.Select(p => p.EndDate));
    }

    [Fact]
    public async Task The_day_is_blocked_when_a_running_package_has_used_its_three_pauses()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        Assert.Equal(D(10, 19), (await PauseAsync(test, id, D(10, 13))).Packages.Single().NewEndDate); // Sat 17 → Mon 19
        Assert.Equal(D(10, 20), (await PauseAsync(test, id, D(10, 15))).Packages.Single().NewEndDate);
        var third = (await PauseAsync(test, id, D(10, 19))).Packages.Single();
        Assert.Equal((D(10, 21), 3, 0), (third.NewEndDate, third.PausedCount, third.PausesLeft));

        // A second package that still has pauses left: the day is still blocked, nothing changes.
        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 12), "OATS"));
        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, D(10, 20)));
        Assert.Contains("Veg Medium ×1 · Weekly", blocked.Message);
        Assert.Contains("already used its 3 pauses", blocked.Message);

        var packages = await PackagesAsync(test, id);
        Assert.Equal([(3, D(10, 21)), (0, D(11, 10))], packages.Select(p => (p.PausedCount, p.EndDate)));
        await using var check = test.CreateContext();
        Assert.Equal(3, await check.PackagePauses.CountAsync());
    }

    [Theory]
    [InlineData(10, 18, "Sunday")]                // not a delivery day
    [InlineData(10, 6, "today or a future")]      // in the past (today is 7 Oct)
    [InlineData(10, 10, "no package delivering")] // before the start date
    [InlineData(10, 24, "no package delivering")] // after the end date
    public async Task Invalid_pause_dates_are_rejected(int month, int day, string expected)
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, D(month, day)));
        Assert.Contains(expected, error.Message);
        Assert.Equal((0, D(10, 17)), (await PackagesAsync(test, id)).Select(p => (p.PausedCount, p.EndDate)).Single());
    }

    [Fact]
    public async Task The_same_day_is_not_paused_twice_but_a_newly_added_package_can_be()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        await PauseAsync(test, id, D(10, 14));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, D(10, 14)));
        Assert.Contains("already paused", error.Message);

        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("WEEKLY", D(10, 13), "OATS"));
        var second = await PauseAsync(test, id, D(10, 14));
        Assert.Equal("Veg Oats ×1 · Weekly (13 Oct 2026 – 20 Oct 2026)", second.Packages.Single().Package); // shown with its new end date
        Assert.Equal([1, 1], (await PackagesAsync(test, id)).Select(p => p.PausedCount));
    }

    [Theory]
    [InlineData("INACTIVE")]
    [InlineData("PAUSED")]
    public async Task Only_active_customers_can_pause(string statusCode)
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        await using (var db = test.CreateContext())
        {
            var detail = await test.Queries(db).GetDetailAsync(id);
            await test.Commands(db).UpdateAsync(id, CustomerTests.Update(detail, r => r.StatusId = test.Entry(DictionaryType.CustomerStatus, statusCode)));
        }

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, D(10, 14)));
        Assert.Contains("Only active customers", error.Message);
        Assert.Equal(0, (await PackagesAsync(test, id)).Single().PausedCount);
    }

    [Fact]
    public async Task A_concurrent_save_of_a_paused_package_fails_instead_of_overwriting()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        await using var racing = test.CreateContext();
        var loadedBeforePause = await racing.CustomerPackages.SingleAsync(p => p.CustomerId == id);

        await PauseAsync(test, id, D(10, 14));

        loadedBeforePause.PausedCount++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => racing.SaveChangesAsync());
    }

    [Fact]
    public async Task Pauses_inside_the_package_survive_a_start_date_change_and_a_renewal_starts_at_zero()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        await PauseAsync(test, id, D(10, 14)); // Wed 14 → end Mon 19
        var packageId = (await PackagesAsync(test, id)).Single().Id;

        await using var db = test.CreateContext();
        var p = await db.CustomerPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
        UpdatePackageRequest Move(DateOnly start, DateTime updatedAt) => new()
        {
            PreferenceId = p.PreferenceId, MealTypeId = p.MealTypeId, Quantity = p.Quantity, FrequencyId = p.FrequencyId, StartDate = start,
            ExpectedUpdatedAt = updatedAt, ConfirmScheduleChange = true
        };

        // Tue 13 … with Wed 14 still paused: 13, 15, 16, 17, 19, 20 → Tue 20.
        var moved = await test.Packages(db).UpdateAsync(packageId, Move(D(10, 13), p.UpdatedAt));
        Assert.Equal((D(10, 20), 1), (moved.EndDate, moved.PausedCount));

        var current = await db.CustomerPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
        var renewed = await test.Packages(db).UpdateAsync(packageId, Move(D(10, 26), current.UpdatedAt));
        Assert.Equal((D(10, 31), 0), (renewed.EndDate, renewed.PausedCount)); // Mon 26 – Sat 31, the old pause is outside
    }
}
