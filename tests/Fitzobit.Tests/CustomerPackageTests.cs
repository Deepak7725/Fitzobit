using System.Text.Json;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Several packages per customer: add, edit (end date recalculated with confirmation), delete before the first delivery.</summary>
public class CustomerPackageTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    /// <summary>A customer with one weekly package Mon 12 – Sat 17 Oct 2026 (today is Wednesday 7 October).</summary>
    private static async Task<int> CustomerAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(test.NewCustomer())).Id;
    }

    private static async Task<UpdatePackageRequest> EditRequestAsync(TestDatabase test, int packageId, Action<UpdatePackageRequest> change)
    {
        await using var db = test.CreateContext();
        var p = await db.CustomerPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
        var r = new UpdatePackageRequest
        {
            PreferenceId = p.PreferenceId, MealTypeId = p.MealTypeId, Quantity = p.Quantity, FrequencyId = p.FrequencyId, StartDate = p.StartDate,
            ExpectedUpdatedAt = p.UpdatedAt
        };
        change(r);
        return r;
    }

    [Fact]
    public async Task A_customer_can_have_several_packages_running_at_the_same_time()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);

        await using var db = test.CreateContext();
        var added = await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 13), "OATS", "WITH-EGG", quantity: 2));
        Assert.Equal((D(10, 13), D(11, 11)), (added.StartDate, added.EndDate)); // Tue 13 Oct + 26 delivery days

        var detail = await test.Queries(db).GetDetailAsync(id);
        Assert.Equal(["Veg Medium ×1 · Weekly", "With Egg Oats ×2 · Monthly"],
            detail.Packages.Select(p => $"{p.Preference} {p.MealType} ×{p.Quantity} · {p.Frequency}"));
        Assert.All(detail.Packages, p => Assert.True(p.CanDelete)); // both start after today
        Assert.Equal([6, 26], detail.Packages.Select(p => p.DeliveryDaysLeft));
        Assert.Equal(AuditActions.CustomerPackageAdded, (await db.AuditLogs.OrderBy(a => a.Id).LastAsync()).Action);

        var list = await test.Queries(db).ListAsync(new CustomerFilter(), 1, 25);
        var row = Assert.Single(list.Items);
        Assert.Equal((D(10, 12), D(11, 11), 2), (row.StartDate, row.EndDate, row.Packages.Count)); // earliest start, latest end
        Assert.Equal("With Egg Oats ×2 · Monthly (13 Oct 2026 – 11 Nov 2026)", row.Packages[1].Text);
    }

    [Fact]
    public async Task Editing_a_package_recalculates_its_end_date_after_confirmation()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);
        int packageId;
        await using (var db = test.CreateContext()) packageId = (await db.CustomerPackages.SingleAsync(p => p.CustomerId == id)).Id;
        test.Clock.UtcNow = test.Clock.UtcNow.AddMinutes(5);

        var quantity = await EditRequestAsync(test, packageId, r => { r.Quantity = 3; r.MealTypeId = test.Entry(DictionaryType.MealType, "LARGE"); });
        await using (var db = test.CreateContext())
        {
            var saved = await test.Packages(db).UpdateAsync(packageId, quantity);
            Assert.Equal(D(10, 17), saved.EndDate); // no schedule change, no confirmation needed
        }

        var monthly = await EditRequestAsync(test, packageId, r => r.FrequencyId = test.Entry(DictionaryType.Frequency, "MONTHLY"));
        await using (var db = test.CreateContext())
        {
            var unconfirmed = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Packages(db).UpdateAsync(packageId, monthly));
            Assert.Contains("Confirm", unconfirmed.Message);
            monthly.ConfirmScheduleChange = true;
            Assert.Equal(D(11, 10), (await test.Packages(db).UpdateAsync(packageId, monthly)).EndDate); // Mon 12 Oct + 26 delivery days
        }

        await using var check = test.CreateContext();
        var package = await check.CustomerPackages.Include(p => p.MealType).SingleAsync(p => p.Id == packageId);
        Assert.Equal(("Large", 3, D(11, 10)), (package.MealType!.DisplayName, package.Quantity, package.EndDate));
        var audit = await check.AuditLogs.Where(a => a.Action == AuditActions.CustomerPackageUpdated).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audit.Count);
        var old = JsonDocument.Parse(audit[1].OldValues!).RootElement;
        Assert.Equal(("Weekly", "2026-10-17"), (old.GetProperty("Frequency").GetString(), old.GetProperty("EndDate").GetString()));

        // An editor still holding the first UpdatedAt cannot overwrite the later change.
        var stale = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Packages(check).UpdateAsync(packageId, quantity));
        Assert.Contains("changed by test-user", stale.Message);
    }

    [Fact]
    public async Task A_package_can_be_deleted_only_before_its_first_delivery_day()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test); // starts Mon 12 Oct, after today
        int future, started;
        await using (var db = test.CreateContext())
        {
            future = (await db.CustomerPackages.SingleAsync(p => p.CustomerId == id)).Id;
            started = (await test.Packages(db).AddAsync(id, test.NewPackage("WEEKLY", D(10, 5)))).Id; // started Mon 5 Oct
        }

        await using var check = test.CreateContext();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Packages(check).DeleteAsync(started));
        Assert.Contains("started on 05 Oct 2026", error.Message);

        await test.Packages(check).DeleteAsync(future);
        Assert.Equal([started], await check.CustomerPackages.Select(p => p.Id).ToListAsync());
        Assert.Contains(await check.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == AuditActions.CustomerPackageDeleted);
        Assert.False((await test.Queries(check).GetDetailAsync(id)).Packages.Single().CanDelete);
    }

    [Fact]
    public async Task Quantity_and_paused_count_are_also_checked_by_the_database()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);

        await using (var db = test.CreateContext())
        {
            var package = await db.CustomerPackages.SingleAsync(p => p.CustomerId == id);
            package.Quantity = CustomerPackage.MaxQuantity + 1;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = test.CreateContext())
        {
            var package = await db.CustomerPackages.SingleAsync(p => p.CustomerId == id);
            package.PausedCount = CustomerPackage.MaxPauses + 1;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}
