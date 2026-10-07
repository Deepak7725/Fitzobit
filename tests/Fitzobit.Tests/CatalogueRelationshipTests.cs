using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

public class CatalogueRelationshipTests
{
    private static CatalogueItem Item(string name = "Large Fit Box - 6 Days") => new()
    {
        Name = name,
        Category = "6-Day Fit Box Trial",
        DisplayPrice = 899m,
        BillingPeriod = BillingPeriod.Weekly,
        PricingTiers =
        {
            new CataloguePricingTier { TierName = "Veg Plan", Price = 899m, BillingPeriod = BillingPeriod.Weekly, MealCount = 6 },
            new CataloguePricingTier { TierName = "Egg Plan", Price = 999m, BillingPeriod = BillingPeriod.Weekly, MealCount = 6 }
        }
    };

    [Fact]
    public async Task Pricing_tiers_belong_to_their_catalogue_item()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            db.CatalogueItems.Add(Item());
            await db.SaveChangesAsync();
        }

        await using (var db = test.CreateContext())
        {
            var item = await db.CatalogueItems.Include(i => i.PricingTiers).SingleAsync();
            Assert.Equal(2, item.PricingTiers.Count);
            Assert.All(item.PricingTiers, t => Assert.Equal(item.Id, t.CatalogueItemId));
            Assert.Equal(999m, item.PricingTiers.Single(t => t.TierName == "Egg Plan").Price);
        }
    }

    [Fact]
    public async Task Deleting_an_item_removes_its_tiers()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            db.CatalogueItems.Add(Item());
            await db.SaveChangesAsync();
        }

        await using (var db = test.CreateContext())
        {
            db.CatalogueItems.Remove(await db.CatalogueItems.SingleAsync());
            await db.SaveChangesAsync();
            Assert.Equal(0, await db.CataloguePricingTiers.CountAsync());
        }
    }

    [Fact]
    public async Task A_tier_cannot_reference_a_missing_item()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        db.CataloguePricingTiers.Add(new CataloguePricingTier { CatalogueItemId = 999, TierName = "Orphan", Price = 1m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Tier_names_are_unique_within_an_item()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var item = Item();
        item.PricingTiers.Add(new CataloguePricingTier { TierName = "Veg Plan", Price = 1m });
        db.CatalogueItems.Add(item);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Catalogue_item_names_are_unique()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        db.CatalogueItems.AddRange(Item("Same"), Item("Same"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Plans_link_to_catalogue_items_and_customers_link_to_plans()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            var item = Item();
            var plan = new Plan { Name = item.Name, Code = "LARGE-FIT-BOX-6-DAYS", CatalogueItem = item };
            var customer = new Customer { CustomerId = "CUST001", CustomerName = "Divya S", Plan = plan };
            customer.SetPhoneNumber("9898989898");
            db.AddRange(item, plan, customer);
            await db.SaveChangesAsync();
        }

        await using (var db = test.CreateContext())
        {
            var customer = await db.Customers.Include(c => c.Plan!).ThenInclude(p => p.CatalogueItem).SingleAsync();
            Assert.Equal("LARGE-FIT-BOX-6-DAYS", customer.Plan!.Code);
            Assert.Equal("Large Fit Box - 6 Days", customer.Plan.CatalogueItem!.Name);
        }
    }
}
