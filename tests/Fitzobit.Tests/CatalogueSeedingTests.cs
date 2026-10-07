using Fitzobit.Application.Catalogue;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fitzobit.Tests;

/// <summary>Seeds from the real Fitzobit_WhatsApp_Catalogue.xlsx in the repository root.</summary>
public class CatalogueSeedingTests
{
    private const string MediumMonthly = "Medium Fit Box Subscription (5 Items) - monthly";
    private const string SaladsMonthly = "Standalone high Protein Salads - monthly- 26 days";
    private const string SaladsWeekly = "Standalone high Protein Salads - weekly - 6 days";

    private static async Task<CatalogueSeedResult> SeedAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        var seeder = new CatalogueSeeder(db, TestPaths.WorkbookSource(), test.AuditFor(db), NullLogger<CatalogueSeeder>.Instance);
        return await seeder.SeedAsync();
    }

    [Fact]
    public async Task Seeding_imports_the_real_catalogue()
    {
        using var test = new TestDatabase();
        var result = await SeedAsync(test);

        Assert.False(result.Skipped);
        Assert.Equal(10, result.ItemsCreated);
        Assert.Equal(18, result.TiersCreated);
        Assert.Equal(10, result.PlansCreated);

        await using var db = test.CreateContext();
        var items = await db.CatalogueItems.Include(i => i.PricingTiers).ToListAsync();
        Assert.Equal(10, items.Count);
        Assert.Equal(
            ["6-Day Fit Box Trial", "Monthly Fit Box", "Oats & Muesli", "Protein Salads"],
            items.Select(i => i.Category!).Distinct().OrderBy(c => c));
        Assert.All(items, i => Assert.Equal("INR", i.Currency));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));

        var medium = items.Single(i => i.Name == MediumMonthly);
        Assert.Equal(BillingPeriod.Monthly, medium.BillingPeriod);
        Assert.Equal(26, medium.MealCount);
        Assert.Equal(4, medium.PricingTiers.Count);
        Assert.Contains("Veg Plan: ₹2,699/month", medium.Description);

        var largeTrial = items.Single(i => i.Name == "Large Fit Box - 6 Days");
        Assert.Equal(BillingPeriod.Weekly, largeTrial.BillingPeriod);
        Assert.Equal([899m, 999m], largeTrial.PricingTiers.Select(t => t.Price).OrderBy(p => p));

        Assert.Equal(10, await db.Plans.CountAsync(p => p.CatalogueItemId != null));
    }

    [Fact]
    public async Task Source_prices_are_preserved_even_when_inconsistent()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);

        await using var db = test.CreateContext();
        var prices = (await db.CatalogueItems.ToListAsync()).ToDictionary(i => i.Name, i => i.DisplayPrice);
        Assert.Equal(699m, prices[MediumMonthly]);
        Assert.Equal(2299m, prices[SaladsMonthly]);
        Assert.Equal(2299m, prices[SaladsWeekly]);
    }

    [Fact]
    public async Task Seeding_twice_creates_no_duplicates()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        var second = await SeedAsync(test);

        Assert.Equal(0, second.ItemsCreated);
        Assert.Equal(0, second.ItemsUpdated);
        Assert.Equal(10, second.ItemsUnchanged);
        Assert.Equal(0, second.TiersCreated + second.TiersUpdated + second.TiersRemoved);
        Assert.Equal(0, second.PlansCreated);

        await using var db = test.CreateContext();
        Assert.Equal(10, await db.CatalogueItems.CountAsync());
        Assert.Equal(18, await db.CataloguePricingTiers.CountAsync());
        Assert.Equal(10, await db.Plans.CountAsync());
        Assert.Equal(10, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.CatalogueItemCreated));
    }

    [Fact]
    public async Task Seeding_restores_source_values_that_were_changed_in_the_database()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using (var db = test.CreateContext())
        {
            (await db.CatalogueItems.SingleAsync(i => i.Name == MediumMonthly)).DisplayPrice = 2699m;
            await db.SaveChangesAsync();
        }

        var result = await SeedAsync(test);

        Assert.Equal(1, result.ItemsUpdated);
        await using var check = test.CreateContext();
        Assert.Equal(699m, (await check.CatalogueItems.SingleAsync(i => i.Name == MediumMonthly)).DisplayPrice);
        Assert.Equal(1, await check.AuditLogs.CountAsync(a => a.Action == AuditActions.CatalogueItemChanged));
    }

    [Fact]
    public async Task Inconsistencies_are_flagged_as_warnings()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);

        await using var db = test.CreateContext();
        var overview = await new CatalogueService(db).GetOverviewAsync();
        var flagged = overview.Items.Where(i => i.Warnings.Count > 0).ToDictionary(i => i.Name);

        Assert.Equal([MediumMonthly, SaladsMonthly, SaladsWeekly], flagged.Keys.OrderBy(k => k));
        Assert.Equal(CatalogueWarningEvaluator.PriceMismatch, Assert.Single(flagged[MediumMonthly].Warnings).Code);
        Assert.Equal(CatalogueWarningEvaluator.PeriodPriceConflict, Assert.Single(flagged[SaladsWeekly].Warnings).Code);
        Assert.Equal(3, overview.ItemsWithWarnings);
    }

    [Fact]
    public async Task Missing_workbook_skips_seeding()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var source = new ExcelSourceFor(Path.Combine(Path.GetTempPath(), "does-not-exist.xlsx"));
        var result = await new CatalogueSeeder(db, source, test.AuditFor(db), NullLogger<CatalogueSeeder>.Instance).SeedAsync();
        Assert.True(result.Skipped);
        Assert.Equal(0, await db.CatalogueItems.CountAsync());
    }

    private sealed class ExcelSourceFor(string path) : ICatalogueSource
    {
        public Task<CatalogueSnapshot?> LoadAsync(CancellationToken cancellationToken = default) =>
            new Infrastructure.Catalogue.ExcelCatalogueSource(
                Microsoft.Extensions.Options.Options.Create(new Infrastructure.Catalogue.CatalogueSeedOptions { FilePath = path }),
                NullLogger<Infrastructure.Catalogue.ExcelCatalogueSource>.Instance).LoadAsync(cancellationToken);
    }
}
