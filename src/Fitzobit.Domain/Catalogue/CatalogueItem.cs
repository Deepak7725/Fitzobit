using Fitzobit.Domain.Common;

namespace Fitzobit.Domain.Catalogue;

/// <summary>
/// One item of the Fitzobit WhatsApp Business catalogue, stored exactly as the source shows it.
/// Source values (including apparent price inconsistencies) are never corrected here.
/// </summary>
public class CatalogueItem : ITimestamped
{
    public int Id { get; set; }

    /// <summary>Exact title from the source catalogue. Natural key for idempotent seeding.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }

    /// <summary>Fitzobit's own wording for the offering type (e.g. "Subscription", "Plan").</summary>
    public string? Term { get; set; }

    public string? Description { get; set; }
    public decimal DisplayPrice { get; set; }
    public string Currency { get; set; } = "INR";
    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Unspecified;

    /// <summary>Billing/duration text exactly as written in the source.</summary>
    public string? DurationText { get; set; }
    public int? DurationDays { get; set; }
    public int? MealCount { get; set; }

    public string? IncludedItems { get; set; }
    public string? Availability { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Where the record came from (file / channel / snapshot date).</summary>
    public string? Source { get; set; }

    /// <summary>Notes recorded alongside the source data (e.g. flagged inconsistencies).</summary>
    public string? SourceNotes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CataloguePricingTier> PricingTiers { get; set; } = new List<CataloguePricingTier>();
}

public enum BillingPeriod
{
    Unspecified,
    Weekly,
    Monthly
}

/// <summary>A price option written inside a catalogue item (e.g. "Veg Plan ₹2,699/month").</summary>
public class CataloguePricingTier
{
    public int Id { get; set; }
    public int CatalogueItemId { get; set; }
    public CatalogueItem? CatalogueItem { get; set; }

    public string TierName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Unspecified;
    public int? MealCount { get; set; }
    public string? Description { get; set; }
    public string? Source { get; set; }
}
