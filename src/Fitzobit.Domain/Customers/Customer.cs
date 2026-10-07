using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Plans;

namespace Fitzobit.Domain.Customers;

public class Customer : IUserTracked
{
    public int Id { get; set; }

    /// <summary>Business identifier supplied by Fitzobit (e.g. "CUST001"). Unique.</summary>
    public string CustomerId { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Phone number exactly as supplied (kept as text).</summary>
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? NormalizedPhoneNumber { get; private set; }

    /// <summary>Location/address exactly as supplied.</summary>
    public string? Location { get; private set; }
    public string? NormalizedLocation { get; private set; }

    public string? Pincode { get; set; }
    public string? Locality { get; set; }
    public string? City { get; set; }

    /// <summary>Plan text exactly as supplied in the import file.</summary>
    public string? OriginalPlan { get; set; }

    public int? PlanId { get; set; }
    public Plan? Plan { get; set; }

    /// <summary>The specific pricing tier (e.g. "Veg Plan") when the supplied plan identified one.</summary>
    public int? PricingTierId { get; set; }
    public CataloguePricingTier? PricingTier { get; set; }

    /// <summary>The import that created this customer, if any.</summary>
    public int? SourceImportBatchId { get; set; }

    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<CustomerDeliveryGroupAssignment> GroupAssignments { get; set; } = new List<CustomerDeliveryGroupAssignment>();

    public void SetPhoneNumber(string phoneNumber)
    {
        PhoneNumber = phoneNumber.Trim();
        NormalizedPhoneNumber = Normalization.NormalizePhone(phoneNumber);
    }

    /// <param name="location">The original value, stored as supplied (outer whitespace trimmed).</param>
    /// <param name="normalizedLocation">A pre-computed normalized form; defaults to <see cref="Normalization.NormalizeLocation"/>.</param>
    public void SetLocation(string? location, string? normalizedLocation = null)
    {
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        NormalizedLocation = normalizedLocation ?? Normalization.NormalizeLocation(location);
    }
}

public enum CustomerStatus
{
    Active,
    Inactive
}
