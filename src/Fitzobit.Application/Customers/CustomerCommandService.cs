using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Imports.Validation;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

public sealed class UpdateCustomerRequest
{
    public string CustomerName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string? Location { get; set; }
    public int? PlanId { get; set; }
    public int? PricingTierId { get; set; }

    /// <summary>Active | Inactive</summary>
    public string Status { get; set; } = "Active";

    /// <summary>The UpdatedAt value the editor started from. A different stored value means someone else changed the customer.</summary>
    public DateTime ExpectedUpdatedAt { get; set; }
}

public sealed record UpdateCustomerResult(bool Changed, IReadOnlyList<string> ChangedFields, IReadOnlyList<string> Warnings);

/// <summary>
/// Edits a customer. CustomerID is the business key and cannot be changed here. Every change is validated with the same
/// rules as the import, checked against concurrent edits, and audited with old and new values.
/// </summary>
public sealed class CustomerCommandService(IFitzobitDbContext db, ILocationClassificationService locations, IAuditLogger audit)
{
    public async Task<UpdateCustomerResult> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException($"Customer {id} was not found.");

        if (Math.Abs((customer.UpdatedAt - DateTime.SpecifyKind(request.ExpectedUpdatedAt, DateTimeKind.Utc)).TotalMilliseconds) > 1)
            throw new BusinessRuleException(
                $"{customer.CustomerId} was changed by {customer.UpdatedBy ?? "another user"} after you opened it. Reload the customer and apply your changes again.");

        var warnings = new List<string>();
        var name = string.Join(' ', (request.CustomerName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 2 or > CustomerRowValidator.MaxNameLength || !name.Any(char.IsLetter))
            throw new BusinessRuleException($"Customer name must be 2–{CustomerRowValidator.MaxNameLength} characters and contain letters.");

        var phone = PhoneNumberValidator.Validate(request.PhoneNumber);
        switch (phone.Check)
        {
            case PhoneCheck.Missing: throw new BusinessRuleException("Phone number is required.");
            case PhoneCheck.ValidMobile: break;
            case PhoneCheck.Landline: warnings.Add("The phone number looks like a landline, not a mobile number."); break;
            default: throw new BusinessRuleException("Invalid phone number: expected a 10-digit Indian mobile number.");
        }
        if (phone.Normalized != customer.NormalizedPhoneNumber &&
            await db.Customers.AnyAsync(c => c.Id != id && c.NormalizedPhoneNumber == phone.Normalized, ct))
            warnings.Add("Another customer already has this phone number.");

        if (string.IsNullOrWhiteSpace(request.Location)) throw new BusinessRuleException("Location is required.");
        var location = locations.Classify(request.Location);
        if (location.Pincode is null) warnings.Add("Pincode not detected in the new location.");

        if (!Enum.TryParse<CustomerStatus>(request.Status, ignoreCase: false, out var status))
            throw new BusinessRuleException("Status must be Active or Inactive.");

        if (request.PlanId is { } planId)
        {
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
                       ?? throw new BusinessRuleException("The selected plan does not exist.");
            if (plan.Status != RecordStatus.Active && planId != customer.PlanId)
                throw new BusinessRuleException("The selected plan is inactive.");
            if (request.PricingTierId is { } tierId &&
                !await db.CataloguePricingTiers.AnyAsync(t => t.Id == tierId && t.CatalogueItemId == plan.CatalogueItemId, ct))
                throw new BusinessRuleException("The selected variant does not belong to the selected plan.");
        }
        else if (request.PricingTierId is not null) throw new BusinessRuleException("Choose a plan before choosing a variant.");

        var changes = new ChangeSet();
        changes.Track("CustomerName", customer.CustomerName, name);
        changes.Track("PhoneNumber", customer.PhoneNumber, request.PhoneNumber.Trim());
        changes.Track("Location", customer.Location, request.Location.Trim());
        changes.Track("Pincode", customer.Pincode, location.Pincode);
        changes.Track("PlanId", customer.PlanId, request.PlanId);
        changes.Track("PricingTierId", customer.PricingTierId, request.PricingTierId);
        changes.Track("Status", customer.Status.ToString(), status.ToString());

        if (!changes.HasChanges) return new UpdateCustomerResult(false, [], warnings);

        customer.CustomerName = name;
        customer.SetPhoneNumber(request.PhoneNumber);
        customer.SetLocation(request.Location, location.NormalizedLocation);
        customer.Pincode = location.Pincode;
        customer.Locality = location.Locality;
        customer.City = location.City;
        customer.PlanId = request.PlanId;
        customer.PricingTierId = request.PricingTierId;
        customer.Status = status;

        audit.RecordChange(AuditActions.CustomerUpdated, nameof(Customer), customer.CustomerId,
            $"Customer updated: {customer.CustomerId} ({string.Join(", ", changes.New.Keys)})", changes.Old, changes.New);
        await db.SaveChangesAsync(ct);
        return new UpdateCustomerResult(true, changes.New.Keys.ToList(), warnings);
    }
}
