using Fitzobit.Application.Catalogue;
using Fitzobit.Domain.Catalogue;

namespace Fitzobit.Tests;

public class CatalogueRulesTests
{
    private static CatalogueItem Item(string name, decimal price, BillingPeriod period, string category = "Boxes",
        params (string Name, decimal Price, BillingPeriod Period)[] tiers)
    {
        var item = new CatalogueItem { Name = name, DisplayPrice = price, BillingPeriod = period, Category = category };
        foreach (var t in tiers)
            item.PricingTiers.Add(new CataloguePricingTier { TierName = t.Name, Price = t.Price, BillingPeriod = t.Period });
        return item;
    }

    [Fact]
    public void Display_price_matching_a_same_period_tier_is_fine()
    {
        var item = Item("Regular", 2199m, BillingPeriod.Monthly, tiers: [("Veg Plan", 2199m, BillingPeriod.Monthly), ("Egg Plan", 2399m, BillingPeriod.Monthly)]);
        Assert.Empty(CatalogueWarningEvaluator.Evaluate(item, [item]));
    }

    [Fact]
    public void Display_price_matching_only_a_different_period_is_a_mismatch()
    {
        var item = Item("Medium", 699m, BillingPeriod.Monthly, tiers:
            [("Veg Plan", 2699m, BillingPeriod.Monthly), ("Weekly Trial (6 Days) - Veg", 699m, BillingPeriod.Weekly)]);
        var warning = Assert.Single(CatalogueWarningEvaluator.Evaluate(item, [item]));
        Assert.Equal(CatalogueWarningEvaluator.PriceMismatch, warning.Code);
        Assert.Contains("Weekly Trial (6 Days) - Veg", warning.Message);
    }

    [Fact]
    public void Weekly_item_priced_like_the_monthly_item_is_flagged_on_both()
    {
        var monthly = Item("Salads monthly", 2299m, BillingPeriod.Monthly, "Salads");
        var weekly = Item("Salads weekly", 2299m, BillingPeriod.Weekly, "Salads");
        var other = Item("Oats weekly", 649m, BillingPeriod.Weekly, "Oats");
        CatalogueItem[] all = [monthly, weekly, other];

        Assert.Single(CatalogueWarningEvaluator.Evaluate(monthly, all));
        Assert.Single(CatalogueWarningEvaluator.Evaluate(weekly, all));
        Assert.Empty(CatalogueWarningEvaluator.Evaluate(other, all));
    }

    [Fact]
    public void Cheaper_weekly_item_is_fine()
    {
        var monthly = Item("Oats monthly", 2549m, BillingPeriod.Monthly, "Oats");
        var weekly = Item("Oats weekly", 649m, BillingPeriod.Weekly, "Oats");
        Assert.Empty(CatalogueWarningEvaluator.Evaluate(weekly, [monthly, weekly]));
    }

    [Theory]
    [InlineData("Monthly (26 meals, Mon–Sat)", BillingPeriod.Monthly, 26, null)]
    [InlineData("Weekly - 6 days (Mon–Sat)", BillingPeriod.Weekly, null, 6)]
    [InlineData("Monthly - 26 days", BillingPeriod.Monthly, null, 26)]
    [InlineData("6 Days (6 meals)", BillingPeriod.Unspecified, 6, 6)]
    public void Duration_text_is_parsed_only_for_what_it_states(string text, BillingPeriod period, int? meals, int? days)
    {
        Assert.Equal(period, CatalogueTextParser.ParseItemPeriod(text));
        Assert.Equal(meals, CatalogueTextParser.ParseMeals(text));
        Assert.Equal(days, CatalogueTextParser.ParseDays(text));
    }

    [Fact]
    public void Item_period_falls_back_to_the_fitzobit_term()
    {
        Assert.Equal(BillingPeriod.Weekly, CatalogueTextParser.ParseItemPeriod("6 Days (6 meals)", "Fit Box - 6 Days (Weekly Trial)"));
    }

    [Theory]
    [InlineData("per month", "Veg Plan", BillingPeriod.Weekly, BillingPeriod.Monthly)]
    [InlineData("6 days", "Weekly Trial (6 Days) - Veg", BillingPeriod.Monthly, BillingPeriod.Weekly)]
    [InlineData("6 days", "Veg Plan", BillingPeriod.Weekly, BillingPeriod.Weekly)]
    public void Tier_period_uses_its_own_wording_before_the_parent(string periodText, string tierName, BillingPeriod parent, BillingPeriod expected) =>
        Assert.Equal(expected, CatalogueTextParser.ParseTierPeriod(periodText, tierName, parent));
}
