using System.Globalization;
using Fitzobit.Domain.Catalogue;

namespace Fitzobit.Application.Catalogue;

public sealed record CatalogueWarning(string Code, string Title, string Message);

/// <summary>
/// Detects apparent inconsistencies in source catalogue data. It only reports; it never decides which value
/// is correct and never changes the data.
/// </summary>
public static class CatalogueWarningEvaluator
{
    public const string PriceMismatch = "PRICE_MISMATCH";
    public const string PeriodPriceConflict = "PERIOD_PRICE_CONFLICT";

    private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

    public static IReadOnlyList<CatalogueWarning> Evaluate(CatalogueItem item, IEnumerable<CatalogueItem> allItems)
    {
        var warnings = new List<CatalogueWarning>();
        CheckDisplayPriceAgainstTiers(item, warnings);
        CheckWeeklyVersusMonthly(item, allItems, warnings);
        return warnings;
    }

    /// <summary>The displayed price should equal one of the tiers written for the same billing period.</summary>
    private static void CheckDisplayPriceAgainstTiers(CatalogueItem item, List<CatalogueWarning> warnings)
    {
        if (item.BillingPeriod == BillingPeriod.Unspecified) return;

        var samePeriod = item.PricingTiers
            .Where(t => t.BillingPeriod == item.BillingPeriod && t.Currency == item.Currency)
            .OrderBy(t => t.Price)
            .ToList();
        if (samePeriod.Count == 0 || samePeriod.Any(t => t.Price == item.DisplayPrice)) return;

        var listed = string.Join(", ", samePeriod.Select(t => $"{t.TierName} {Money(t.Price, t.Currency)}"));
        var message = $"Displayed price {Money(item.DisplayPrice, item.Currency)} does not match any {item.BillingPeriod.ToString().ToLowerInvariant()} " +
                      $"price in the description ({listed}).";

        var otherMatch = item.PricingTiers.FirstOrDefault(t => t.BillingPeriod != item.BillingPeriod && t.Price == item.DisplayPrice);
        if (otherMatch is not null)
            message += $" It equals the \"{otherMatch.TierName}\" price.";

        warnings.Add(new CatalogueWarning(PriceMismatch, "Price mismatch", message));
    }

    /// <summary>Within a category, a weekly item should cost less than the monthly item it is a shorter version of.</summary>
    private static void CheckWeeklyVersusMonthly(CatalogueItem item, IEnumerable<CatalogueItem> allItems, List<CatalogueWarning> warnings)
    {
        if (item.BillingPeriod == BillingPeriod.Unspecified || string.IsNullOrWhiteSpace(item.Category)) return;

        var counterparts = allItems.Where(o =>
            !ReferenceEquals(o, item)
            && o.Category == item.Category
            && o.Currency == item.Currency
            && o.BillingPeriod != BillingPeriod.Unspecified
            && o.BillingPeriod != item.BillingPeriod);

        foreach (var other in counterparts)
        {
            var (weekly, monthly) = item.BillingPeriod == BillingPeriod.Weekly ? (item, other) : (other, item);
            if (weekly.DisplayPrice < monthly.DisplayPrice) continue;

            var relation = weekly.DisplayPrice == monthly.DisplayPrice ? "the same displayed price as" : "a higher displayed price than";
            warnings.Add(new CatalogueWarning(PeriodPriceConflict, "Weekly/monthly price conflict",
                $"Weekly item \"{weekly.Name}\" ({Money(weekly.DisplayPrice, weekly.Currency)}) has {relation} " +
                $"monthly item \"{monthly.Name}\" ({Money(monthly.DisplayPrice, monthly.Currency)})."));
        }
    }

    public static string Money(decimal amount, string currency) =>
        currency == "INR"
            ? "₹" + amount.ToString("N2", IndianCulture)
            : $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
