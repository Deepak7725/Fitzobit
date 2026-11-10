using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Money;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fitzobit.Tests;

/// <summary>
/// Money page (owner and Siva, 2026-11-10; Manager accounts only): costs per box, target profit per frequency, the price list seeded from
/// the WhatsApp catalogue, list prices on new packages (× quantity), Manager-only price edits, and earnings per period.
/// </summary>
public class MoneyTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private int Id(TestDatabase test, DictionaryType type, string code) => test.Entry(type, code);

    [Fact]
    public async Task The_migration_seeds_costs_targets_and_catalogue_prices()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var money = test.Money(db);

        var settings = await money.GetSettingsAsync();
        Assert.Equal((30m, 25m, new DateOnly(2000, 1, 1)), (settings.DeliveryCostPerBox, settings.HelperCostPerBox, settings.EffectiveFrom));
        Assert.Empty(settings.Lines);
        Assert.Equal(30m, settings.Targets.Single(t => t.Frequency == "Monthly").Percent);
        Assert.Equal(40m, settings.Targets.Single(t => t.Frequency == "Weekly").Percent);

        var list = await money.GetPriceListAsync();
        decimal? Price(string meal, string pref, string freq) => list.Prices.SingleOrDefault(p =>
            p.MealTypeId == Id(test, DictionaryType.MealType, meal) && p.PreferenceId == Id(test, DictionaryType.Preference, pref)
            && p.FrequencyId == Id(test, DictionaryType.Frequency, freq))?.Price;
        Assert.Equal(3499m, Price("MEDIUM", "VEG", "MONTHLY"));
        Assert.Equal(3499m, Price("MEDIUM", "WITH-EGG", "MONTHLY")); // Egg costs the same as Veg
        Assert.Equal(1149m, Price("LARGE", "WITH-EGG", "WEEKLY"));
        Assert.Equal(3499m, Price("OATS", "NONE", "MONTHLY"));
        Assert.Null(Price("MEAL-BOWL", "VEG", "MONTHLY")); // not in the catalogue: not sold
        Assert.Equal(24, list.Prices.Count); // 4 boxes × 3 preferences × weekly/monthly
    }

    [Fact]
    public async Task The_owner_s_own_meal_types_are_priced_by_name()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            foreach (var name in new[] { "Oats with whey protein", "Detox drink" })
                await test.Dictionaries(db).CreateAsync(DictionaryType.MealType, new Application.Dictionaries.SaveDictionaryEntryRequest { DisplayName = name });
            await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261105060851_UnlimitedPauses"); // before MoneyModule
            await db.Database.MigrateAsync();
        }

        await using var check = test.CreateContext();
        var prices = await check.PackagePrices.AsNoTracking().Select(p => new { Meal = p.MealType!.DisplayName, Frequency = p.Frequency!.Code, p.Price }).ToListAsync();
        Assert.Equal([1199m, 3849m], prices.Where(p => p.Meal == "Oats with whey protein").GroupBy(p => p.Frequency).OrderByDescending(g => g.Key)
            .Select(g => g.Distinct().Single().Price));
        Assert.Equal(3, prices.Count(p => p.Meal == "Detox drink" && p.Frequency == "MONTHLY" && p.Price == 1049m));
        Assert.Equal(3, prices.Count(p => p.Meal == "Detox drink" && p.Frequency == "WEEKLY" && p.Price == 333m));
    }

    [Fact]
    public async Task Settings_and_prices_are_saved_validated_and_audited_without_amounts()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var money = test.Money(db);
        var monthly = Id(test, DictionaryType.Frequency, "MONTHLY");

        var saved = await money.SaveSettingsAsync(new SaveMoneySettingsRequest
            { DeliveryCostPerBox = 32, HelperCostPerBox = 25, Targets = [new SaveProfitTarget { FrequencyId = monthly, Percent = 35 }] });
        Assert.Equal((32m, 35m), (saved.DeliveryCostPerBox, saved.Targets.Single(t => t.FrequencyId == monthly).Percent));
        Assert.Contains("between 0 % and 95 %", (await Assert.ThrowsAsync<BusinessRuleException>(() => money.SaveSettingsAsync(new SaveMoneySettingsRequest
            { DeliveryCostPerBox = 30, HelperCostPerBox = 25, Targets = [new SaveProfitTarget { FrequencyId = monthly, Percent = 120 }] }))).Message);

        var (medium, veg, bowl) = (Id(test, DictionaryType.MealType, "MEDIUM"), Id(test, DictionaryType.Preference, "VEG"), Id(test, DictionaryType.MealType, "MEAL-BOWL"));
        var list = await money.SavePriceListAsync(new SavePriceListRequest
        {
            Prices =
            [
                new SavePriceCell { MealTypeId = medium, PreferenceId = veg, FrequencyId = monthly, Price = 2799 },
                new SavePriceCell { MealTypeId = bowl, PreferenceId = veg, FrequencyId = monthly, Price = 1999 },
                new SavePriceCell { MealTypeId = Id(test, DictionaryType.MealType, "OATS"), PreferenceId = Id(test, DictionaryType.Preference, "NONE"), FrequencyId = monthly, Price = null }
            ]
        });
        Assert.Equal(2799m, list.Prices.Single(p => p.MealTypeId == medium && p.PreferenceId == veg && p.FrequencyId == monthly).Price);
        Assert.Contains(list.Prices, p => p.MealTypeId == bowl && p.Price == 1999m);
        Assert.Equal(24, list.Prices.Count); // one added, one removed
        await Assert.ThrowsAsync<BusinessRuleException>(() => money.SavePriceListAsync(new SavePriceListRequest
            { Prices = [new SavePriceCell { MealTypeId = medium, PreferenceId = veg, FrequencyId = monthly, Price = 0 }] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => money.SavePriceListAsync(new SavePriceListRequest
            { Prices = [new SavePriceCell { MealTypeId = veg, PreferenceId = medium, FrequencyId = monthly, Price = 10 }] })); // swapped ids

        var audit = await db.AuditLogs.Where(a => a.Action == AuditActions.SettingsUpdated).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => Assert.DoesNotContain("2799", a.Summary + a.OldValues + a.NewValues + a.Details)); // Admins read the audit log
    }

    [Fact]
    public async Task New_packages_take_the_list_price_times_quantity_and_only_managers_see_or_change_it()
    {
        using var test = new TestDatabase();
        int id;
        await using (var db = test.CreateContext())
        {
            var request = test.NewCustomer(frequency: "MONTHLY");
            request.Packages[0] = test.NewPackage("MONTHLY", D(10, 12), quantity: 2);                // Medium Veg monthly ×2
            request.Packages.Add(test.NewPackage("WEEKLY", D(10, 12), "MEAL-BOWL"));                 // not in the price list
            id = (await test.Commands(db).CreateAsync(request)).Id;
            await test.Packages(db).AddAsync(id, test.NewPackage("WEEKLY", D(10, 19), "LARGE", "WITH-EGG"));
        }

        await using (var db = test.CreateContext())
        {
            var packages = (await test.Queries(db).GetDetailAsync(id)).Packages;
            Assert.Equal([6998m, null, 1149m], packages.Select(p => p.Price));

            // A Manager sets a discount; it is kept as typed and the audit log only says the price changed.
            var monthly = packages[0];
            var stored = await db.CustomerPackages.AsNoTracking().SingleAsync(p => p.Id == monthly.Id);
            await test.Packages(db).UpdateAsync(monthly.Id, Edit(stored, request => request.Price = 5000));
            Assert.Equal(5000m, (await db.CustomerPackages.AsNoTracking().SingleAsync(p => p.Id == monthly.Id)).Price);
            var entry = await db.AuditLogs.OrderBy(a => a.Id).LastAsync(a => a.Action == AuditActions.CustomerPackageUpdated);
            Assert.DoesNotContain("5000", entry.NewValues + entry.OldValues + entry.Summary);
        }

        test.User.Role = "Admin";
        await using (var db = test.CreateContext())
        {
            Assert.All((await test.Queries(db).GetDetailAsync(id)).Packages, p => Assert.Null(p.Price)); // hidden from Admins
            var stored = await db.CustomerPackages.AsNoTracking().Where(p => p.CustomerId == id).OrderBy(p => p.Id).FirstAsync();
            await test.Packages(db).UpdateAsync(stored.Id, Edit(stored, request => request.Price = 1)); // ignored for an Admin
            Assert.Equal(5000m, (await db.CustomerPackages.AsNoTracking().SingleAsync(p => p.Id == stored.Id)).Price);
            stored = await db.CustomerPackages.AsNoTracking().SingleAsync(p => p.Id == stored.Id);
            await test.Packages(db).UpdateAsync(stored.Id, Edit(stored, request => request.Quantity = 3)); // different box count: list price
            Assert.Equal(10497m, (await db.CustomerPackages.AsNoTracking().SingleAsync(p => p.Id == stored.Id)).Price);
        }
    }

    private static UpdatePackageRequest Edit(Domain.Customers.CustomerPackage p, Action<UpdatePackageRequest> change)
    {
        var request = new UpdatePackageRequest
        {
            PreferenceId = p.PreferenceId, MealTypeId = p.MealTypeId, Quantity = p.Quantity, FrequencyId = p.FrequencyId, StartDate = p.StartDate,
            ExpectedUpdatedAt = p.UpdatedAt, Price = p.Price
        };
        change(request);
        return request;
    }

    [Fact]
    public async Task Earnings_count_each_delivered_box_and_leave_out_paused_days_and_unpriced_packages()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var anitha = (await test.Commands(db).CreateAsync(test.NewCustomer(frequency: "MONTHLY"))).Id;                  // Medium Veg monthly ₹3,499
        await test.Pauses(db).PauseAsync(anitha, new PauseCustomerRequest { Dates = [D(10, 14)] });
        var bala = test.NewCustomer("Bala", "9000000002");
        bala.Packages[0] = test.NewPackage("WEEKLY", D(10, 12), "MEAL-BOWL");                                          // no price
        await test.Commands(db).CreateAsync(bala);

        var week = await test.Money(db).EarningsAsync(D(10, 12), D(10, 18)); // Mon 12 – Sun 18 October
        Assert.Equal((6, 6), (week.DeliveryDays, week.UnpricedBoxes));
        var unpricedPackage = Assert.Single(week.UnpricedPackages);
        Assert.Equal(("Bala", "Veg Meal bowl ×1 · Weekly", 6), (unpricedPackage.CustomerName, unpricedPackage.Package, unpricedPackage.Boxes));
        // 5 delivered boxes × 3,499 ÷ 26 = 672.88; delivery 5 × 30, helper 5 × 25; profit 30 % = 201.87; food = the rest.
        var t = week.Totals;
        Assert.Equal((5, 672.88m, 150m, 125m, 201.87m, 196.02m), (t.Boxes, t.Earned, t.DeliveryCost, t.HelperCost, t.TargetProfit, t.FoodBudget));
        Assert.Empty(t.OtherCosts);
        Assert.Equal("Medium · Veg", Assert.Single(week.ByBox).Label);
        Assert.Equal(("No group", 5), (Assert.Single(week.ByGroup).Label, week.ByGroup[0].Boxes));

        var sunday = await test.Money(db).EarningsAsync(D(10, 18), D(10, 18));
        Assert.Equal((0, 0), (sunday.DeliveryDays, sunday.Totals.Boxes));
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Money(db).EarningsAsync(D(10, 18), D(10, 12)));
    }

    [Fact]
    public async Task Costs_keep_their_history_and_each_day_uses_the_costs_valid_then()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var money = test.Money(db);
        var (monthly, weekly) = (Id(test, DictionaryType.Frequency, "MONTHLY"), Id(test, DictionaryType.Frequency, "WEEKLY"));
        await test.Commands(db).CreateAsync(test.NewCustomer(frequency: "MONTHLY")); // Medium Veg monthly ₹3,499 from Mon 12 Oct

        var saved = await money.SaveSettingsAsync(new SaveMoneySettingsRequest
        {
            EffectiveFrom = D(10, 14), DeliveryCostPerBox = 32, HelperCostPerBox = 25,
            Lines = [new SaveCostLine { Name = " Plate ", AmountPerBox = 10 }],
            Targets = [new SaveProfitTarget { FrequencyId = monthly, Percent = 35 }] // weekly not sent: keeps 40 %
        });
        Assert.Equal((D(10, 14), 32m, "Plate", 35m, 40m), (saved.EffectiveFrom, saved.DeliveryCostPerBox, saved.Lines.Single().Name,
            saved.Targets.Single(x => x.FrequencyId == monthly).Percent, saved.Targets.Single(x => x.FrequencyId == weekly).Percent));
        Assert.Equal([D(10, 14), new DateOnly(2000, 1, 1)], saved.History.Select(h => h.EffectiveFrom));
        Assert.Equal(67m, saved.History[0].CostsPerBox);

        var before = await money.GetSettingsAsync(D(10, 13));
        Assert.Equal((30m, 0, 30m), (before.DeliveryCostPerBox, before.Lines.Count, before.Targets.Single(x => x.FrequencyId == monthly).Percent));

        // Saving the same day again replaces that version.
        await money.SaveSettingsAsync(new SaveMoneySettingsRequest
        {
            EffectiveFrom = D(10, 14), DeliveryCostPerBox = 32, HelperCostPerBox = 25,
            Lines = [new SaveCostLine { Name = "Plate", AmountPerBox = 10 }], Targets = [new SaveProfitTarget { FrequencyId = monthly, Percent = 35 }]
        });
        Assert.Equal(2, await db.MoneyCostVersions.CountAsync());

        // Mon 12 – Sat 17 Oct: two days with the old costs, four with the new ones.
        var week = await money.EarningsAsync(D(10, 12), D(10, 17));
        var t = week.Totals;
        Assert.Equal(["Plate"], week.CostNames);
        Assert.Equal((6, 807.46m, 188m, 150m, 40m, 269.15m, 160.31m),
            (t.Boxes, t.Earned, t.DeliveryCost, t.HelperCost, t.OtherCosts["Plate"], t.TargetProfit, t.FoodBudget));
        Assert.Equal($"{Id(test, DictionaryType.MealType, "MEDIUM")}-{Id(test, DictionaryType.Preference, "VEG")}", week.ByBox.Single().Key);

        foreach (var bad in new[]
                 {
                     new SaveMoneySettingsRequest { Lines = [new SaveCostLine { Name = "", AmountPerBox = 1 }] },
                     new SaveMoneySettingsRequest { Lines = [new SaveCostLine { Name = "Plate", AmountPerBox = 1 }, new SaveCostLine { Name = "plate", AmountPerBox = 2 }] },
                     new SaveMoneySettingsRequest { Lines = [new SaveCostLine { Name = "Plate", AmountPerBox = -1 }] }
                 })
            await Assert.ThrowsAsync<BusinessRuleException>(() => money.SaveSettingsAsync(bad));
    }

    [Fact]
    public async Task The_history_migration_keeps_the_costs_saved_before_it()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261110095915_MoneyModule"); // AppSettings era
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "AppSettings" SET "Value" = '33' WHERE "Key" = 'Money.DeliveryCostPerBox';
                UPDATE "AppSettings" SET "Value" = '28' WHERE "Key" = 'Money.ProfitTarget.MONTHLY';
                """);
            await db.Database.MigrateAsync();
        }

        await using var check = test.CreateContext();
        var settings = await test.Money(check).GetSettingsAsync(D(10, 7));
        Assert.Equal((new DateOnly(2000, 1, 1), 33m, 25m), (settings.EffectiveFrom, settings.DeliveryCostPerBox, settings.HelperCostPerBox));
        Assert.Equal(28m, settings.Targets.Single(t => t.Frequency == "Monthly").Percent);
        Assert.Equal(40m, settings.Targets.Single(t => t.Frequency == "Weekly").Percent);
        Assert.False(await check.AppSettings.AnyAsync(s => s.Key.StartsWith("Money.")));
    }

    [Fact]
    public void The_split_matches_the_owner_s_example()
    {
        // Medium Veg monthly ₹2,699 ÷ 26 = ₹103.81 per meal; 30 % profit ₹31.14; ₹30 delivery + ₹25 helper leave ₹17.67 for food.
        var (perMeal, profit, food) = MoneyService.Split(2699m, 26, 30m + 25m, 30m);
        Assert.Equal((103.81m, 31.14m, 17.67m), (Math.Round(perMeal, 2), Math.Round(profit, 2), Math.Round(food, 2)));
    }
}
