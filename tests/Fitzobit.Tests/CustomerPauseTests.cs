using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>
/// Pausing delivery days: only packages whose frequency can pause (Monthly by default) are paused; several days can be paused in
/// one request; each package's first 3 paused days move its end date to the next delivery day (never a Sunday), later paused days
/// are skipped without moving it.
/// </summary>
public class CustomerPauseTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    /// <summary>A customer with a monthly package Mon 12 Oct – Tue 10 Nov 2026 (26 delivery days; today is Wednesday 7 October).</summary>
    private static async Task<int> MonthlyCustomerAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(test.NewCustomer(frequency: "MONTHLY"))).Id;
    }

    /// <summary>A customer with a weekly package Mon 12 – Sat 17 October 2026.</summary>
    private static async Task<int> WeeklyCustomerAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(test.NewCustomer())).Id;
    }

    private static async Task<PauseResultDto> PauseAsync(TestDatabase test, int id, DateOnly[] dates, string? reason = null)
    {
        await using var db = test.CreateContext();
        return await test.Pauses(db).PauseAsync(id, new PauseCustomerRequest { Dates = [.. dates], Reason = reason });
    }

    private static Task<PauseResultDto> PauseAsync(TestDatabase test, int id, DateOnly date, string? reason = null) => PauseAsync(test, id, [date], reason);

    private static async Task<List<CustomerPackage>> PackagesAsync(TestDatabase test, int id)
    {
        await using var db = test.CreateContext();
        return await db.CustomerPackages.AsNoTracking().Include(p => p.Pauses).Where(p => p.CustomerId == id).OrderBy(p => p.StartDate).ThenBy(p => p.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task Pausing_a_monthly_day_moves_the_meal_to_the_next_delivery_day()
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);

        var result = await PauseAsync(test, id, D(10, 17), "Going out of town");

        var paused = Assert.Single(result.Packages);
        Assert.Equal((D(11, 10), D(11, 11), 1, 2), (paused.PreviousEndDate, paused.NewEndDate, paused.PausedCount, paused.ExtendingPausesLeft));
        Assert.Equal([new PausedDayDto(D(10, 17), true)], paused.Days);
        Assert.Empty(result.NotPaused);
        var package = Assert.Single(await PackagesAsync(test, id));
        Assert.Equal((D(10, 12), D(11, 11), 1), (package.StartDate, package.EndDate, package.PausedCount));
        var pause = Assert.Single(package.Pauses);
        Assert.Equal((D(10, 17), "Going out of town", "test-user", true), (pause.PausedDate, pause.Reason, pause.CreatedBy, pause.ExtendedEndDate));

        await using var db = test.CreateContext();
        Assert.Equal(AuditActions.CustomerPaused, (await db.AuditLogs.OrderBy(a => a.Id).LastAsync()).Action);
        var detail = (await test.Queries(db).GetDetailAsync(id)).Packages.Single();
        Assert.Equal((26, 1, 3, true), (detail.DeliveryDaysLeft, detail.PausedCount, detail.MaxPauses, detail.CanPause)); // still 26 meals
    }

    [Fact]
    public async Task Several_days_in_one_go_only_the_first_three_extend_the_end_date()
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);

        // Tue 13 – Sat 17 October, sent out of order.
        var result = await PauseAsync(test, id, [D(10, 16), D(10, 13), D(10, 15), D(10, 17), D(10, 14), D(10, 14)]);

        Assert.Equal([D(10, 13), D(10, 14), D(10, 15), D(10, 16), D(10, 17)], result.Dates);
        var paused = Assert.Single(result.Packages);
        Assert.Equal([true, true, true, false, false], paused.Days.Select(d => d.ExtendedEndDate));
        Assert.Equal((D(11, 10), D(11, 13), 3, 0), (paused.PreviousEndDate, paused.NewEndDate, paused.PausedCount, paused.ExtendingPausesLeft));

        // Counted per package over all time: a later pause no longer moves the end date.
        var later = Assert.Single((await PauseAsync(test, id, D(10, 20))).Packages);
        Assert.Equal((D(11, 13), D(11, 13), false), (later.PreviousEndDate, later.NewEndDate, later.Days.Single().ExtendedEndDate));

        var package = Assert.Single(await PackagesAsync(test, id));
        Assert.Equal((D(11, 13), 3, 6), (package.EndDate, package.PausedCount, package.Pauses.Count));
        Assert.Equal([true, true, true, false, false, false], package.Pauses.OrderBy(p => p.PausedDate).Select(p => p.ExtendedEndDate));
    }

    [Fact]
    public async Task Weekly_packages_are_not_paused_and_keep_delivering()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, D(10, 14)));
        Assert.Contains("Veg Medium ×1 · Weekly (12 Oct 2026 – 17 Oct 2026) cannot be paused", error.Message);

        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 12), "OATS"));
        var result = await PauseAsync(test, id, D(10, 14));

        Assert.Equal("Veg Oats ×1 · Monthly (12 Oct 2026 – 11 Nov 2026)", Assert.Single(result.Packages).Package);
        Assert.Equal(["Veg Medium ×1 · Weekly (12 Oct 2026 – 17 Oct 2026)"], result.NotPaused);
        var packages = await PackagesAsync(test, id);
        Assert.Equal([(D(10, 17), 0, 0), (D(11, 11), 1, 1)], packages.Select(p => (p.EndDate, p.PausedCount, p.Pauses.Count)));
    }

    [Fact]
    public async Task The_can_pause_switch_decides_which_frequencies_pause()
    {
        using var test = new TestDatabase();
        var id = await WeeklyCustomerAsync(test);
        await using (var db = test.CreateContext())
        {
            var weekly = await db.DictionaryEntries.SingleAsync(e => e.DictionaryType == DictionaryType.Frequency && e.Code == "WEEKLY");
            await test.Dictionaries(db).UpdateAsync(weekly.Id,
                new SaveDictionaryEntryRequest { DisplayName = weekly.DisplayName, NumericValue = weekly.NumericValue, CanPause = true });
        }

        var paused = Assert.Single((await PauseAsync(test, id, D(10, 14))).Packages);
        Assert.Equal(D(10, 19), paused.NewEndDate); // Sat 17 → Mon 19
    }

    [Theory]
    [InlineData(10, 18, "Sunday")]                // not a delivery day
    [InlineData(10, 6, "today or future")]        // in the past (today is 7 Oct)
    [InlineData(10, 10, "no package delivering")] // before the start date
    [InlineData(11, 12, "no package delivering")] // after the end date (Wed 11 Nov once 14 Oct is paused)
    public async Task Invalid_pause_dates_are_rejected_and_nothing_is_saved(int month, int day, string expected)
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);

        // A valid day sent together with an invalid one: the whole request is refused.
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, [D(10, 14), D(month, day)]));
        Assert.Contains(expected, error.Message);
        Assert.Equal((0, D(11, 10), 0), (await PackagesAsync(test, id)).Select(p => (p.PausedCount, p.EndDate, p.Pauses.Count)).Single());
    }

    [Fact]
    public async Task At_least_one_day_is_required()
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, []));
        Assert.Contains("at least one day", error.Message);
    }

    [Fact]
    public async Task The_same_day_is_not_paused_twice_but_a_newly_added_package_can_be()
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);
        await PauseAsync(test, id, D(10, 14));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PauseAsync(test, id, D(10, 14)));
        Assert.Contains("already paused", error.Message);

        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 13), "OATS"));
        var second = await PauseAsync(test, id, D(10, 14));
        Assert.Equal("Veg Oats ×1 · Monthly (13 Oct 2026 – 12 Nov 2026)", second.Packages.Single().Package); // shown with its new end date
        Assert.Equal([1, 1], (await PackagesAsync(test, id)).Select(p => p.PausedCount));
    }

    [Fact]
    public async Task A_preview_shows_the_result_without_saving()
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);

        await using (var db = test.CreateContext())
        {
            var preview = await test.Pauses(db).PreviewAsync(id, new PauseCustomerRequest { Dates = [D(10, 13), D(10, 14)] });
            var package = Assert.Single(preview.Packages);
            Assert.Equal((D(11, 10), D(11, 12), 2), (package.PreviousEndDate, package.NewEndDate, package.PausedCount));
        }

        var saved = Assert.Single(await PackagesAsync(test, id));
        Assert.Equal((D(11, 10), 0, 0), (saved.EndDate, saved.PausedCount, saved.Pauses.Count));
        await using var check = test.CreateContext();
        Assert.DoesNotContain(await check.AuditLogs.ToListAsync(), a => a.Action == AuditActions.CustomerPaused);
    }

    [Theory]
    [InlineData("INACTIVE")]
    [InlineData("PAUSED")]
    public async Task Only_active_customers_can_pause(string statusCode)
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);
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
        var id = await MonthlyCustomerAsync(test);
        await using var racing = test.CreateContext();
        var loadedBeforePause = await racing.CustomerPackages.SingleAsync(p => p.CustomerId == id);

        await PauseAsync(test, id, D(10, 14));

        loadedBeforePause.PausedCount++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => racing.SaveChangesAsync());
    }

    [Fact]
    public async Task A_start_date_change_keeps_the_first_three_pauses_and_a_renewal_starts_at_zero()
    {
        using var test = new TestDatabase();
        var id = await MonthlyCustomerAsync(test);
        await PauseAsync(test, id, [D(10, 13), D(10, 14), D(10, 15), D(10, 16), D(10, 17)]); // end Tue 10 → Fri 13 Nov
        var packageId = (await PackagesAsync(test, id)).Single().Id;

        await using var db = test.CreateContext();
        var p = await db.CustomerPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
        UpdatePackageRequest Move(DateOnly start, DateTime updatedAt) => new()
        {
            PreferenceId = p.PreferenceId, MealTypeId = p.MealTypeId, Quantity = p.Quantity, FrequencyId = p.FrequencyId, StartDate = start,
            ExpectedUpdatedAt = updatedAt, ConfirmScheduleChange = true
        };

        // From Tue 13 Oct, 26 delivery days end Wed 11 Nov; the first three pauses (13, 14, 15) move it to Sat 14 Nov.
        var moved = await test.Packages(db).UpdateAsync(packageId, Move(D(10, 13), p.UpdatedAt));
        Assert.Equal((D(11, 14), 3), (moved.EndDate, moved.PausedCount));

        var current = await db.CustomerPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
        var renewed = await test.Packages(db).UpdateAsync(packageId, Move(D(10, 26), current.UpdatedAt));
        Assert.Equal((D(11, 24), 0), (renewed.EndDate, renewed.PausedCount)); // Mon 26 Oct – Tue 24 Nov, the old pauses are outside
    }
}
