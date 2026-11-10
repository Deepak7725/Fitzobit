using Fitzobit.Domain.Common;
using Fitzobit.Domain.Dictionaries;

namespace Fitzobit.Domain.Money;

/// <summary>
/// The Money page's costs and profit targets valid from <see cref="EffectiveFrom"/> until the next version starts (owner, 2026-11-10:
/// keep history, a "Valid from" date when saving). A delivery day uses the version with the latest <see cref="EffectiveFrom"/> on or
/// before it, so changing a cost never rewrites earlier days.
/// </summary>
public class MoneyCostVersion : IUserTracked
{
    public const decimal MaxCostPerBox = 10_000m;

    public int Id { get; set; }

    /// <summary>First day these values apply (unique).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Delivery person + petrol, per box delivered.</summary>
    public decimal DeliveryCostPerBox { get; set; }

    /// <summary>Helper, per box delivered.</summary>
    public decimal HelperCostPerBox { get; set; }

    public ICollection<MoneyCostLine> Lines { get; set; } = new List<MoneyCostLine>();
    public ICollection<MoneyProfitTarget> Targets { get; set; } = new List<MoneyProfitTarget>();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A cost the Manager added by name, per box delivered, for all boxes (e.g. "Plate" ₹10).</summary>
public class MoneyCostLine
{
    public const int MaxNameLength = 60;

    public int Id { get; set; }
    public int VersionId { get; set; }
    public MoneyCostVersion? Version { get; set; }
    public string Name { get; set; } = "";
    public decimal AmountPerBox { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Target profit for one frequency, as a percentage of the selling price.</summary>
public class MoneyProfitTarget
{
    public const decimal MaxPercent = 95m;

    public int Id { get; set; }
    public int VersionId { get; set; }
    public MoneyCostVersion? Version { get; set; }
    public int FrequencyId { get; set; }
    public DictionaryEntry? Frequency { get; set; }
    public decimal Percent { get; set; }
}
