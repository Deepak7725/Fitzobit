using Fitzobit.Domain.Catalogue;

namespace Fitzobit.Domain.Plans;

/// <summary>
/// A manager-confirmed mapping from plan text used in customer files (e.g. "Medium Box Veg") to a Fitzobit plan
/// and, optionally, a pricing tier. Reused automatically by later imports.
/// </summary>
public class PlanAlias
{
    public int Id { get; set; }

    /// <summary>Normalized match key (see PlanTextNormalizer). Unique.</summary>
    public string AliasKey { get; set; } = string.Empty;

    /// <summary>The text as it first appeared in a file.</summary>
    public string OriginalText { get; set; } = string.Empty;

    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    public int? PricingTierId { get; set; }
    public CataloguePricingTier? PricingTier { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
