using System.Text.RegularExpressions;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Application.Geocoding;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>Issues the business id of a new customer. Must be safe when several customers are created at the same time.</summary>
public interface ICustomerIdGenerator
{
    /// <summary>The next unused id, e.g. "FITZ-000042".</summary>
    Task<string> NextAsync(CancellationToken ct = default);
}

/// <summary>Customer fields on the Add/Edit customer screens. CustomerID is never taken from the client.</summary>
public class SaveCustomerRequest
{
    public string CustomerName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";

    /// <summary>Optional second number; same rules as <see cref="PhoneNumber"/> and different from it.</summary>
    public string? AlternatePhoneNumber { get; set; }

    public string? Address { get; set; }
    public string? Pincode { get; set; }

    /// <summary>Optional Google Maps link (google.com/maps…, maps.google.…, maps.app.goo.gl/…, goo.gl/maps/…).</summary>
    public string? GoogleMapUrl { get; set; }

    /// <summary>The time of day deliveries are wanted (required).</summary>
    public TimeOnly? PreferredDeliveryTime { get; set; }

    /// <summary>Defaults to Active when creating.</summary>
    public int? StatusId { get; set; }
}

/// <summary>A new customer with at least one package and an optional first note.</summary>
public sealed class CreateCustomerRequest : SaveCustomerRequest
{
    public List<SavePackageRequest> Packages { get; set; } = [];

    /// <summary>Saved as the customer's first dated note.</summary>
    public string? Note { get; set; }
}

public sealed class UpdateCustomerRequest : SaveCustomerRequest
{
    /// <summary>The UpdatedAt value the editor started from. A different stored value means someone else changed the customer.</summary>
    public DateTime ExpectedUpdatedAt { get; set; }
}

public sealed record CreateCustomerResult(int Id, string CustomerId, IReadOnlyList<PackageCreatedDto> Packages, IReadOnlyList<string> Warnings);

public sealed record PackageCreatedDto(int Id, DateOnly StartDate, DateOnly EndDate);

public sealed record UpdateCustomerResult(bool Changed, IReadOnlyList<string> ChangedFields, IReadOnlyList<string> Warnings);

/// <summary>
/// Creates and edits customers. The CustomerID is generated on the server and never changes. A new customer needs a preferred
/// delivery time and at least one package (packages are then managed by <see cref="CustomerPackageService"/>). Every change is
/// validated, checked against concurrent edits and audited with old and new values.
/// </summary>
public sealed partial class CustomerCommandService(
    IFitzobitDbContext db,
    ILocationClassificationService locations,
    DictionaryService dictionaries,
    CustomerPackageService packages,
    ICustomerIdGenerator idGenerator,
    IGeocodingQueue geocodingQueue,
    IAuditLogger audit)
{
    public const int MaxNameLength = 150;
    public const int MaxAddressLength = 500;
    public const int MaxGoogleMapUrlLength = 2000;

    public async Task<CreateCustomerResult> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var v = await ValidateAsync(request, null, warnings, ct);

        if (request.Packages is not { Count: > 0 }) throw new BusinessRuleException("Add at least one package.");
        var validatedPackages = new List<CustomerPackageService.ValidatedPackage>();
        for (var i = 0; i < request.Packages.Count; i++)
        {
            try { validatedPackages.Add(await packages.ValidateAsync(request.Packages[i], null, ct)); }
            catch (BusinessRuleException e) when (request.Packages.Count > 1) { throw new BusinessRuleException($"Package {i + 1}: {e.Message}"); }
        }
        var note = CustomerNoteService.ValidateText(request.Note, required: false);

        var customer = new Customer();
        Apply(customer, v);
        customer.CustomerId = await idGenerator.NextAsync(ct);
        foreach (var p in validatedPackages) customer.Packages.Add(CustomerPackageService.NewPackage(p));
        if (note is not null) customer.Notes.Add(new CustomerNote { Text = note });

        db.Customers.Add(customer);
        var snapshot = Snapshot(customer, v);
        snapshot["Packages"] = validatedPackages.Select(CustomerPackageService.Describe).ToList();
        if (note is not null) snapshot["Note"] = note;
        audit.RecordChange(AuditActions.CustomerCreated, nameof(Customer), customer.CustomerId,
            $"Customer created: {customer.CustomerId} {customer.CustomerName}", new Dictionary<string, object?>(), snapshot);
        await db.SaveChangesAsync(ct);
        geocodingQueue.Notify(); // the location is looked up in the background; saving never waits for it
        return new CreateCustomerResult(customer.Id, customer.CustomerId,
            customer.Packages.Select(p => new PackageCreatedDto(p.Id, p.StartDate, p.EndDate)).ToList(), warnings);
    }

    public async Task<UpdateCustomerResult> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException($"Customer {id} was not found.");

        if (Math.Abs((customer.UpdatedAt - DateTime.SpecifyKind(request.ExpectedUpdatedAt, DateTimeKind.Utc)).TotalMilliseconds) > 1)
            throw new BusinessRuleException(
                $"{customer.CustomerId} was changed by {customer.UpdatedBy ?? "another user"} after you opened it. Reload the customer and apply your changes again.");

        var warnings = new List<string>();
        var v = await ValidateAsync(request, customer, warnings, ct);
        var oldStatus = await db.DictionaryEntries.AsNoTracking().Where(e => e.Id == customer.StatusId).Select(e => e.DisplayName).FirstAsync(ct);

        var changes = new ChangeSet();
        changes.Track("CustomerName", customer.CustomerName, v.Name);
        changes.Track("PhoneNumber", customer.PhoneNumber, v.Phone);
        changes.Track("AlternatePhoneNumber", customer.AlternatePhoneNumber, v.AlternatePhone);
        changes.Track("Address", customer.Address, v.Address);
        changes.Track("Pincode", customer.Pincode, v.Pincode);
        changes.Track("GoogleMapUrl", customer.GoogleMapUrl, v.GoogleMapUrl);
        changes.Track("PreferredDeliveryTime", customer.PreferredDeliveryTime, v.DeliveryTime);
        changes.Track("Status", oldStatus, v.Status.DisplayName);
        if (!changes.HasChanges) return new UpdateCustomerResult(false, [], warnings);

        var locationChanged = customer.Address != v.Address || customer.Pincode != v.Pincode;
        Apply(customer, v);
        if (locationChanged) customer.ResetLocation(); // old coordinates no longer match the address

        audit.RecordChange(AuditActions.CustomerUpdated, nameof(Customer), customer.CustomerId,
            $"Customer updated: {customer.CustomerId} ({string.Join(", ", changes.New.Keys)})", changes.Old, changes.New);
        await db.SaveChangesAsync(ct);
        if (locationChanged) geocodingQueue.Notify();
        return new UpdateCustomerResult(true, changes.New.Keys.ToList(), warnings);
    }

    private sealed record Validated(
        string Name,
        string Phone,
        string? AlternatePhone,
        string Address,
        string? NormalizedAddress,
        string? Pincode,
        string? GoogleMapUrl,
        string? Locality,
        string? City,
        TimeOnly DeliveryTime,
        DictionaryEntry Status);

    /// <param name="existing">The customer being edited, or null when creating. A pincode an existing customer already has
    /// cannot be cleared; customers carried over from the old model may still lack it until someone completes their details.</param>
    private async Task<Validated> ValidateAsync(SaveCustomerRequest r, Customer? existing, List<string> warnings, CancellationToken ct)
    {
        var errors = new List<string>();

        var name = string.Join(' ', (r.CustomerName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 2 or > MaxNameLength || !name.Any(char.IsLetter))
            errors.Add($"Customer name must be 2–{MaxNameLength} characters and contain letters.");

        var phone = PhoneNumberValidator.Validate(r.PhoneNumber);
        switch (phone.Check)
        {
            case PhoneCheck.Missing: errors.Add("Phone number is required."); break;
            case PhoneCheck.ValidMobile: break;
            case PhoneCheck.Landline: warnings.Add("The phone number looks like a landline, not a mobile number."); break;
            default: errors.Add("Invalid phone number: expected a 10-digit Indian mobile number."); break;
        }

        var alternate = PhoneNumberValidator.Validate(r.AlternatePhoneNumber);
        switch (alternate.Check)
        {
            case PhoneCheck.Missing: break;
            case PhoneCheck.ValidMobile or PhoneCheck.Landline when alternate.Normalized == phone.Normalized:
                errors.Add("The alternative phone number must be different from the phone number."); break;
            case PhoneCheck.ValidMobile: break;
            case PhoneCheck.Landline: warnings.Add("The alternative phone number looks like a landline, not a mobile number."); break;
            default: errors.Add("Invalid alternative phone number: expected a 10-digit Indian mobile number."); break;
        }

        var address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim();
        if (address is null) errors.Add("Address is required.");
        else if (address.Length > MaxAddressLength) errors.Add($"Address must be at most {MaxAddressLength} characters.");

        var pincode = string.IsNullOrWhiteSpace(r.Pincode) ? null : r.Pincode.Trim();
        if (pincode is null) { if (existing is null || existing.Pincode is not null) errors.Add("Pincode is required."); }
        else if (!Pincode().IsMatch(pincode)) errors.Add("Pincode must be 6 digits (Indian PIN code).");

        var mapUrl = string.IsNullOrWhiteSpace(r.GoogleMapUrl) ? null : r.GoogleMapUrl.Trim();
        if (mapUrl is not null && !IsGoogleMapUrl(mapUrl))
            errors.Add("The Google Maps link must be a Google Maps address, e.g. https://maps.app.goo.gl/… or https://www.google.com/maps/…");

        if (r.PreferredDeliveryTime is null) errors.Add("Preferred delivery time is required.");
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        var status = r.StatusId is { } s
            ? await dictionaries.ResolveSelectionAsync(DictionaryType.CustomerStatus, s, existing?.StatusId, ct)
            : existing is null
                ? await dictionaries.FindByCodeAsync(DictionaryType.CustomerStatus, CustomerStatusCodes.Active, ct)
                : throw new BusinessRuleException("Status is required.");

        var normalizedPhone = phone.Normalized;
        if (normalizedPhone != existing?.NormalizedPhoneNumber &&
            await db.Customers.AnyAsync(c => c.Id != (existing == null ? 0 : existing.Id) && c.NormalizedPhoneNumber == normalizedPhone, ct))
            warnings.Add("Another customer already has this phone number.");

        var location = locations.Classify(address);
        if (location.Pincode is { } mentioned && pincode is not null && mentioned != pincode)
            warnings.Add($"The address mentions pincode {mentioned}, but the pincode entered is {pincode}.");

        // Minutes are enough for a delivery time; seconds from the client are dropped.
        var time = r.PreferredDeliveryTime!.Value;
        var alternatePhone = alternate.Check == PhoneCheck.Missing ? null : r.AlternatePhoneNumber!.Trim();
        return new Validated(name, r.PhoneNumber.Trim(), alternatePhone, address!, location.NormalizedLocation, pincode, mapUrl, location.Locality, location.City,
            new TimeOnly(time.Hour, time.Minute), status);
    }

    private static void Apply(Customer customer, Validated v)
    {
        customer.CustomerName = v.Name;
        customer.SetPhoneNumber(v.Phone);
        customer.AlternatePhoneNumber = v.AlternatePhone;
        customer.SetAddress(v.Address, v.NormalizedAddress);
        customer.Pincode = v.Pincode;
        customer.GoogleMapUrl = v.GoogleMapUrl;
        customer.Locality = v.Locality;
        customer.City = v.City;
        customer.PreferredDeliveryTime = v.DeliveryTime;
        customer.StatusId = v.Status.Id;
    }

    private static Dictionary<string, object?> Snapshot(Customer c, Validated v) => new()
    {
        ["CustomerName"] = c.CustomerName,
        ["PhoneNumber"] = c.PhoneNumber,
        ["AlternatePhoneNumber"] = c.AlternatePhoneNumber,
        ["Address"] = c.Address,
        ["Pincode"] = c.Pincode,
        ["GoogleMapUrl"] = c.GoogleMapUrl,
        ["PreferredDeliveryTime"] = c.PreferredDeliveryTime,
        ["Status"] = v.Status.DisplayName
    };

    /// <summary>
    /// An http(s) Google Maps address: google.TLD/maps…, maps.google.TLD/…, maps.app.goo.gl/… or goo.gl/maps/… (TLD such as com or
    /// co.in), at most <see cref="MaxGoogleMapUrlLength"/> characters.
    /// </summary>
    public static bool IsGoogleMapUrl(string url)
    {
        if (url.Length > MaxGoogleMapUrlLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        return host == "maps.app.goo.gl"
               || (host == "goo.gl" && path.StartsWith("/maps", StringComparison.OrdinalIgnoreCase))
               || MapsHost().IsMatch(host)
               || (GoogleHost().IsMatch(host) && path.StartsWith("/maps", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"^[1-9][0-9]{5}$")]
    private static partial Regex Pincode();

    [GeneratedRegex(@"^maps\.google\.[a-z]{2,3}(\.[a-z]{2})?$")]
    private static partial Regex MapsHost();

    [GeneratedRegex(@"^(www\.)?google\.[a-z]{2,3}(\.[a-z]{2})?$")]
    private static partial Regex GoogleHost();
}
