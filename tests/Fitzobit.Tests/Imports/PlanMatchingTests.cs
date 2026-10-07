using Fitzobit.Application.Imports.Plans;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests.Imports;

/// <summary>Plan matching against the real Fitzobit catalogue (seeded from the workbook).</summary>
public class PlanMatchingTests : IAsyncLifetime
{
    private readonly TestDatabase _test = new();
    private PlanMatcher _matcher = null!;
    private Dictionary<string, int> _planIds = null!;
    private Dictionary<(int PlanId, string Tier), int> _tierIds = null!;

    public async Task InitializeAsync()
    {
        await ImportHarness.CreateAsync(_test);
        await using var db = _test.CreateContext();
        _matcher = await new PlanCatalogue(db).CreateMatcherAsync();
        _planIds = await db.Plans.ToDictionaryAsync(p => p.Name, p => p.Id);
        _tierIds = (await (from p in db.Plans
                               join t in db.CataloguePricingTiers on p.CatalogueItemId equals (int?)t.CatalogueItemId
                               select new { PlanId = p.Id, t.TierName, t.Id }).ToListAsync())
            .ToDictionary(x => (x.PlanId, x.TierName), x => x.Id);
    }

    public Task DisposeAsync()
    {
        _test.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("Regular Fit Box - 6 Days")]
    [InlineData("regular fit box 6 days")]
    [InlineData("REGULAR-FIT-BOX-6-DAYS")]
    public void Exact_plan_names_and_codes_map(string text)
    {
        var result = _matcher.Match(text);
        Assert.Equal(PlanMappingStatus.Mapped, result.Status);
        Assert.Equal(PlanMatchMethod.ExactName, result.Method);
        Assert.Equal(_planIds["Regular Fit Box - 6 Days"], result.PlanId);
    }

    [Fact]
    public void Keywords_map_when_one_plan_is_clearly_best()
    {
        var result = _matcher.Match("Oats monthly");
        Assert.Equal(PlanMappingStatus.Mapped, result.Status);
        Assert.Equal(PlanMatchMethod.Keywords, result.Method);
        Assert.Equal(_planIds["Oats & Muesli Breakfast monthly Plan - 26 days"], result.PlanId);
    }

    [Fact]
    public void Variant_words_select_the_pricing_tier()
    {
        const string plan = "Medium Fit Box Subscription (5 Items) - monthly";
        var result = _matcher.Match("Medium Fit Box Monthly Veg");

        Assert.Equal(PlanMappingStatus.Mapped, result.Status);
        Assert.Equal(_planIds[plan], result.PlanId);
        Assert.Equal(_tierIds[(_planIds[plan], "Veg Plan")], result.PricingTierId);
    }

    [Fact]
    public void Weekly_trial_wording_prefers_the_6_day_box()
    {
        var result = _matcher.Match("Large Fit Box Weekly Trial Egg");
        Assert.Equal(_planIds["Large Fit Box - 6 Days"], result.PlanId);
        Assert.Equal(_tierIds[(_planIds["Large Fit Box - 6 Days"], "Egg Plan")], result.PricingTierId);
    }

    [Theory]
    [InlineData("Medium Fit Box")]
    [InlineData("Medium Fit Box Veg")]
    [InlineData("Weekly Trial")]
    [InlineData("Monthly")]
    public void Ambiguous_text_is_never_silently_mapped(string text)
    {
        var result = _matcher.Match(text);
        Assert.Equal(PlanMappingStatus.Ambiguous, result.Status);
        Assert.Null(result.PlanId);
        Assert.True(result.CandidatePlanIds.Count >= 2);
    }

    [Theory]
    [InlineData("Gold Membership")]
    [InlineData("Pizza")]
    public void Unknown_text_is_reported(string text) =>
        Assert.Equal(PlanMappingStatus.Unknown, _matcher.Match(text).Status);

    [Fact]
    public void Saved_aliases_win()
    {
        var planId = _planIds["Large Fit Box - 6 Days"];
        var matcher = new PlanMatcher(
            [new PlanInfo(planId, "Large Fit Box - 6 Days", "LARGE-FIT-BOX-6-DAYS", null, null, null, null, [])],
            [new SavedPlanAlias(PlanTextNormalizer.Key("Big box"), planId, null)]);

        var result = matcher.Match("big BOX");
        Assert.Equal((PlanMappingStatus.Mapped, PlanMatchMethod.SavedAlias, (int?)planId), (result.Status, result.Method, result.PlanId));
    }

    [Fact]
    public void Plan_keys_ignore_order_case_and_punctuation() =>
        Assert.Equal(PlanTextNormalizer.Key("Veg - Medium Box"), PlanTextNormalizer.Key("medium box (VEG)"));
}
