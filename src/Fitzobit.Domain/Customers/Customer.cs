using Fitzobit.Domain.Common;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;

namespace Fitzobit.Domain.Customers;

public class Customer : IUserTracked
{
    public int Id { get; set; }

    /// <summary>Business identifier, unique. New customers get "FITZ-000001" style ids; ids of existing customers are kept as they were.</summary>
    public string CustomerId { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Phone number exactly as supplied (kept as text).</summary>
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? NormalizedPhoneNumber { get; private set; }

    /// <summary>Optional second phone number (as supplied); other customers may share it (e.g. family members).</summary>
    public string? AlternatePhoneNumber { get; set; }

    /// <summary>Delivery address exactly as supplied.</summary>
    public string? Address { get; private set; }
    public string? NormalizedAddress { get; private set; }

    public string? Pincode { get; set; }

    /// <summary>Optional Google Maps link to the customer's place, as supplied (only stored and shown; locations still come from the address).</summary>
    public string? GoogleMapUrl { get; set; }

    /// <summary>Recognised from the address text, for grouping and reporting.</summary>
    public string? Locality { get; set; }
    public string? City { get; set; }

    /// <summary>The time of day the customer wants deliveries (one time for all their packages). Required for new customers.</summary>
    public TimeOnly? PreferredDeliveryTime { get; set; }

    public int StatusId { get; set; }
    public DictionaryEntry? Status { get; set; }

    /// <summary>Filled by geocoding (not entered by hand). Null when the location is unknown; never guessed.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public GeocodeStatus GeocodeStatus { get; set; } = GeocodeStatus.Pending;

    /// <summary>How precisely the coordinates were found (Address, Locality or Pincode area).</summary>
    public GeoMatchLevel? GeocodeMatchLevel { get; set; }

    public int GeocodeAttempts { get; set; }

    /// <summary>When a pending lookup may be tried (again); null = as soon as possible.</summary>
    public DateTime? GeocodeNextAttemptAt { get; set; }

    public DateTime? GeocodedAt { get; set; }

    /// <summary>The matched place or the reason the location is unavailable.</summary>
    public string? GeocodeMessage { get; set; }

    /// <summary>Geocoding provider that produced the coordinates.</summary>
    public string? GeocodeSource { get; set; }

    /// <summary>The plan this customer had under the retired catalogue/plan model, kept read-only for reference.</summary>
    public string? LegacyPlan { get; set; }

    /// <summary>PostgreSQL row version (xmin): concurrent saves of the same customer fail instead of overwriting each other.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<CustomerDeliveryGroupAssignment> GroupAssignments { get; set; } = new List<CustomerDeliveryGroupAssignment>();
    /// <summary>The customer's meal packages; several can run at the same time (e.g. lunch monthly and breakfast weekly).</summary>
    public ICollection<CustomerPackage> Packages { get; set; } = new List<CustomerPackage>();

    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();

    /// <summary>Forgets the coordinates (e.g. after an address change) and queues a fresh location lookup.</summary>
    public void ResetLocation()
    {
        Latitude = null;
        Longitude = null;
        GeocodeStatus = GeocodeStatus.Pending;
        GeocodeMatchLevel = null;
        GeocodeAttempts = 0;
        GeocodeNextAttemptAt = null;
        GeocodedAt = null;
        GeocodeMessage = null;
        GeocodeSource = null;
    }

    public void SetPhoneNumber(string phoneNumber)
    {
        PhoneNumber = phoneNumber.Trim();
        NormalizedPhoneNumber = Normalization.NormalizePhone(phoneNumber);
    }

    /// <param name="address">The original value, stored as supplied (outer whitespace trimmed).</param>
    /// <param name="normalizedAddress">A pre-computed normalized form; defaults to <see cref="Normalization.NormalizeLocation"/>.</param>
    public void SetAddress(string? address, string? normalizedAddress = null)
    {
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        NormalizedAddress = normalizedAddress ?? Normalization.NormalizeLocation(address);
    }
}

public enum GeocodeStatus
{
    /// <summary>Waiting for a (re)try.</summary>
    Pending,
    Located,
    /// <summary>The geocoder found no match in the service area.</summary>
    NotFound,
    /// <summary>Gave up after repeated errors; a user can retry.</summary>
    Failed
}

public enum GeoMatchLevel
{
    /// <summary>The street address (building or street).</summary>
    Address,
    /// <summary>The locality/neighbourhood in the address.</summary>
    Locality,
    /// <summary>Only the pincode area: approximate.</summary>
    Pincode
}

/// <summary>
/// One meal package (order) of a customer: what is delivered (preference, meal type, quantity), how often (frequency = number of
/// delivery days) and when (start date; the end date is calculated, may be set by hand, and is extended by pauses). A customer
/// may have several.
/// </summary>
public class CustomerPackage : IUserTracked
{
    /// <summary>Paused days that extend the end date; any further paused days are skipped without moving it.</summary>
    public const int MaxPauses = 3;

    public const int MaxQuantity = 20;

    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int PreferenceId { get; set; }
    public DictionaryEntry? Preference { get; set; }

    public int MealTypeId { get; set; }
    public DictionaryEntry? MealType { get; set; }

    /// <summary>Boxes per delivery (1–<see cref="MaxQuantity"/>).</summary>
    public int Quantity { get; set; } = 1;

    public int FrequencyId { get; set; }
    public DictionaryEntry? Frequency { get; set; }

    /// <summary>First delivery day (never a Sunday).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last delivery day: calculated in delivery days (or set by hand) and extended by the first pauses.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Paused days that extended the end date (0–<see cref="MaxPauses"/>); <see cref="Pauses"/> holds every paused day.</summary>
    public int PausedCount { get; set; }

    /// <summary>
    /// When the package moved to the customer's past packages (by the daily 12 pm run on its last day). Past packages are read-only and
    /// left out of the Customers list, filters, exports, counts and pauses; null = a current package.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    public bool IsPast => ArchivedAt != null;

    /// <summary>When someone last pressed "Send reminder" for this package's renewal, and who (display name).</summary>
    public DateTime? RenewalReminderAt { get; set; }
    public string? RenewalReminderBy { get; set; }

    /// <summary>PostgreSQL row version (xmin): concurrent changes of the same package fail instead of overwriting each other.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<PackagePause> Pauses { get; set; } = new List<PackagePause>();

    /// <summary>Whether <paramref name="date"/> lies inside the package period.</summary>
    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;
}

/// <summary>
/// One paused delivery day of a package. For the package's first <see cref="CustomerPackage.MaxPauses"/> paused days the meal is
/// not lost: the end date moves to the next delivery day. Later paused days are skipped and the end date stays
/// (<see cref="NewEndDate"/> = <see cref="PreviousEndDate"/>). Only packages whose frequency allows pausing are paused.
/// </summary>
public class PackagePause
{
    public int Id { get; set; }

    public int PackageId { get; set; }
    public CustomerPackage? Package { get; set; }

    /// <summary>The delivery day that will not be delivered.</summary>
    public DateOnly PausedDate { get; set; }

    public DateOnly PreviousEndDate { get; set; }
    public DateOnly NewEndDate { get; set; }

    /// <summary>Whether this pause moved the end date.</summary>
    public bool ExtendedEndDate => NewEndDate != PreviousEndDate;

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>A dated note about a customer (e.g. "Gate code 1234, call before delivery"). Writers edit/delete their own; admins any.</summary>
public class CustomerNote : IUserTracked
{
    public const int MaxLength = 2000;

    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>User name of the writer (decides who may edit or delete the note).</summary>
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
