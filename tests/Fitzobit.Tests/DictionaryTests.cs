using Fitzobit.Application.Common;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Drop-down values come from DictionaryEntries: seeded by the migration, managed in Settings, safe to deactivate.</summary>
public class DictionaryTests
{
    private static SaveDictionaryEntryRequest Request(string name, int? days = null, bool active = true) =>
        new() { DisplayName = name, NumericValue = days, IsActive = active };

    [Fact]
    public async Task Migration_seeds_every_dropdown_in_order()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();

        var options = await test.Dictionaries(db).GetActiveOptionsAsync();

        Assert.Equal(["Veg", "With Egg", "None"], options.Preference.Select(o => o.DisplayName));
        Assert.Equal(["Regular", "Medium", "Large", "Meal bowl", "Oats"], options.MealType.Select(o => o.DisplayName));
        Assert.Equal(["Monthly", "Weekly"], options.Frequency.Select(o => o.DisplayName));
        Assert.Equal([26, 6], options.Frequency.Select(o => o.NumericValue));
        Assert.Equal(["Active", "Inactive", "Paused"], options.CustomerStatus.Select(o => o.DisplayName));
        Assert.Equal(["ACTIVE", "INACTIVE", "PAUSED"], options.CustomerStatus.Select(o => o.Code));
        Assert.All(await db.DictionaryEntries.Where(e => e.DictionaryType == DictionaryType.CustomerStatus).ToListAsync(), e => Assert.True(e.IsSystem));
    }

    [Fact]
    public async Task Values_can_be_added_renamed_reordered_and_deactivated_and_dropdowns_follow()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var dictionaries = test.Dictionaries(db);

        var fortnightly = await dictionaries.CreateAsync(DictionaryType.Frequency, Request("  Fortnightly ", 12));
        Assert.Equal(("FORTNIGHTLY", "Fortnightly", 12, true), (fortnightly.Code, fortnightly.DisplayName, fortnightly.NumericValue, fortnightly.IsActive));

        var bowl = (await dictionaries.ListAsync(DictionaryType.MealType)).Single(e => e.Code == "MEAL-BOWL");
        await dictionaries.UpdateAsync(bowl.Id, Request("Salad bowl"));
        var oats = (await dictionaries.ListAsync(DictionaryType.MealType)).Single(e => e.Code == "OATS");
        await dictionaries.UpdateAsync(oats.Id, Request("Oats", active: false));

        var mealIds = (await dictionaries.ListAsync(DictionaryType.MealType)).Select(e => e.Id).Reverse().ToList();
        await dictionaries.ReorderAsync(DictionaryType.MealType, mealIds);

        var options = await dictionaries.GetActiveOptionsAsync();
        Assert.Equal(["Monthly", "Weekly", "Fortnightly"], options.Frequency.Select(o => o.DisplayName));
        Assert.Equal(["Salad bowl", "Large", "Medium", "Regular"], options.MealType.Select(o => o.DisplayName));
        Assert.Equal("MEAL-BOWL", (await db.DictionaryEntries.AsNoTracking().SingleAsync(e => e.Id == bowl.Id)).Code); // codes never change

        var all = await dictionaries.ListAsync(DictionaryType.MealType);
        Assert.False(all.Single(e => e.Id == oats.Id).IsActive);
        Assert.Contains(await db.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == AuditActions.DictionaryEntryUpdated);
        Assert.Contains(await db.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == AuditActions.DictionaryEntryCreated);
    }

    [Fact]
    public async Task Invalid_values_are_rejected()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var dictionaries = test.Dictionaries(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.CreateAsync(DictionaryType.Preference, Request("   ")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.CreateAsync(DictionaryType.Preference, Request("veg"))); // duplicate name
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.CreateAsync(DictionaryType.Frequency, Request("Daily")));     // no day count
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.CreateAsync(DictionaryType.Frequency, Request("Daily", 0)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.CreateAsync(DictionaryType.MealType, Request(new string('x', 101))));
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.ReorderAsync(DictionaryType.Preference, [1, 2]));

        var weekly = test.Entry(DictionaryType.Frequency, "WEEKLY");
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.UpdateAsync(weekly, Request("Weekly", null)));
        Assert.Equal(13, await db.DictionaryEntries.CountAsync());
    }

    [Fact]
    public async Task System_statuses_and_the_last_active_value_stay_active()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var dictionaries = test.Dictionaries(db);

        var active = test.Entry(DictionaryType.CustomerStatus, "ACTIVE");
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.UpdateAsync(active, Request("Active", active: false)));
        Assert.Contains("must stay active", error.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.DeleteAsync(active));
        await dictionaries.UpdateAsync(active, Request("Running")); // renaming is fine

        var monthly = test.Entry(DictionaryType.Frequency, "MONTHLY");
        var weekly = test.Entry(DictionaryType.Frequency, "WEEKLY");
        await dictionaries.UpdateAsync(monthly, Request("Monthly", 26, active: false));
        var last = await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.UpdateAsync(weekly, Request("Weekly", 6, active: false)));
        Assert.Contains("only active", last.Message);
    }

    [Fact]
    public async Task Values_used_by_customers_are_deactivated_never_deleted()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
            await test.Commands(db).CreateAsync(test.NewCustomer());

        await using (var db = test.CreateContext())
        {
            var dictionaries = test.Dictionaries(db);
            var veg = test.Entry(DictionaryType.Preference, "VEG");
            var none = test.Entry(DictionaryType.Preference, "NONE");

            Assert.Equal(1, (await dictionaries.ListAsync(DictionaryType.Preference)).Single(e => e.Id == veg).CustomerCount);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => dictionaries.DeleteAsync(veg));
            Assert.Contains("used by 1 package", error.Message);

            await dictionaries.UpdateAsync(veg, Request("Veg", active: false));
            await dictionaries.DeleteAsync(none);
            Assert.Equal(["VEG", "WITH-EGG"], await db.DictionaryEntries.Where(e => e.DictionaryType == DictionaryType.Preference)
                .OrderBy(e => e.SortOrder).Select(e => e.Code).ToListAsync());
            Assert.Equal(veg, (await db.CustomerPackages.SingleAsync()).PreferenceId); // the package keeps its value
        }
    }

    [Fact]
    public async Task Database_refuses_to_delete_a_value_in_use()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
            await test.Commands(db).CreateAsync(test.NewCustomer());

        await using var other = test.CreateContext();
        var veg = await other.DictionaryEntries.SingleAsync(e => e.Code == "VEG");
        other.DictionaryEntries.Remove(veg);
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
