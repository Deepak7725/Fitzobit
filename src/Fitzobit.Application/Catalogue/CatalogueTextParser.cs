using System.Text.RegularExpressions;
using Fitzobit.Domain.Catalogue;

namespace Fitzobit.Application.Catalogue;

/// <summary>
/// Reads structured values out of the catalogue's own wording. Only values the text states are extracted;
/// anything not stated stays null / Unspecified.
/// </summary>
public static partial class CatalogueTextParser
{
    /// <summary>Billing period from the item's own texts (duration, Fitzobit term, name), first explicit match wins.</summary>
    public static BillingPeriod ParseItemPeriod(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Contains("monthly", StringComparison.OrdinalIgnoreCase)) return BillingPeriod.Monthly;
            if (text.Contains("weekly", StringComparison.OrdinalIgnoreCase)) return BillingPeriod.Weekly;
        }
        return BillingPeriod.Unspecified;
    }

    /// <summary>Tier period: "per month" is monthly, a tier named "Weekly ..." is weekly, otherwise the parent item's period applies.</summary>
    public static BillingPeriod ParseTierPeriod(string? periodText, string tierName, BillingPeriod parentPeriod)
    {
        if (periodText is not null && periodText.Contains("month", StringComparison.OrdinalIgnoreCase)) return BillingPeriod.Monthly;
        if (tierName.Contains("weekly", StringComparison.OrdinalIgnoreCase)) return BillingPeriod.Weekly;
        return parentPeriod;
    }

    public static int? ParseMeals(string? text) => ParseNumberBefore(text, MealsPattern());

    public static int? ParseDays(string? text) => ParseNumberBefore(text, DaysPattern());

    private static int? ParseNumberBefore(string? text, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = pattern.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    [GeneratedRegex(@"(\d+)\s*meals?\b", RegexOptions.IgnoreCase)]
    private static partial Regex MealsPattern();

    [GeneratedRegex(@"(\d+)\s*days?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DaysPattern();
}
