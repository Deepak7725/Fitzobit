using Fitzobit.Domain.Common;

namespace Fitzobit.Domain.Dictionaries;

/// <summary>
/// One administrator-managed value of a customer drop-down (preference, meal type, frequency, status).
/// Entries used by customers are deactivated, never deleted; <see cref="Code"/> is stable, <see cref="DisplayName"/> may change.
/// </summary>
public class DictionaryEntry : IUserTracked
{
    public int Id { get; set; }
    public DictionaryType DictionaryType { get; set; }

    /// <summary>Stable upper-case key (e.g. "WEEKLY"); unique per type and never changed after creation.</summary>
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>System entries can be renamed and reordered but not deactivated or deleted (the application relies on them).</summary>
    public bool IsSystem { get; set; }

    /// <summary>Type-specific number. Frequency: delivery days per package (Monthly 26, Weekly 6).</summary>
    public int? NumericValue { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum DictionaryType
{
    Preference,
    MealType,
    Frequency,
    CustomerStatus
}

/// <summary>Codes of the customer statuses the application relies on.</summary>
public static class CustomerStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
    public const string Paused = "PAUSED";
}
