using Fitzobit.Domain.Common;
using Fitzobit.Domain.Dictionaries;

namespace Fitzobit.Domain.Money;

/// <summary>
/// The price list (Money page, Managers only): what one box of a meal type and preference costs for a whole package of a frequency,
/// e.g. Medium · Veg · Monthly (26 deliveries) = ₹2,699. A package sold with quantity 2 costs twice that. New packages take their
/// price from here; packages already sold keep the price they were sold at (<see cref="Customers.CustomerPackage.Price"/>).
/// </summary>
public class PackagePrice : IUserTracked
{
    public const decimal MaxPrice = 1_000_000m;

    public int Id { get; set; }

    public int MealTypeId { get; set; }
    public DictionaryEntry? MealType { get; set; }

    public int PreferenceId { get; set; }
    public DictionaryEntry? Preference { get; set; }

    public int FrequencyId { get; set; }
    public DictionaryEntry? Frequency { get; set; }

    /// <summary>Package price for one box per delivery day, in rupees.</summary>
    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
