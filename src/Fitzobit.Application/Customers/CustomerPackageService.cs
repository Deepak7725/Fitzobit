using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>The fields of a package entered on the Add Customer screen or the package dialog. The end date is never taken from the client.</summary>
public class SavePackageRequest
{
    public int? PreferenceId { get; set; }
    public int? MealTypeId { get; set; }

    /// <summary>Boxes per delivery (default 1).</summary>
    public int? Quantity { get; set; }

    public int? FrequencyId { get; set; }
    public DateOnly? StartDate { get; set; }
}

public sealed class UpdatePackageRequest : SavePackageRequest
{
    /// <summary>The package's UpdatedAt the editor started from (concurrency check).</summary>
    public DateTime ExpectedUpdatedAt { get; set; }

    /// <summary>Must be true when the start date or frequency changes, because the end date is recalculated.</summary>
    public bool ConfirmScheduleChange { get; set; }
}

public sealed record PackageSavedDto(int Id, DateOnly StartDate, DateOnly EndDate, int PausedCount);

/// <summary>
/// A customer's packages: add, edit and delete. Each package has its own preference, meal type, quantity, frequency and dates; the
/// end date is calculated in delivery days (<see cref="DeliveryScheduleService"/>) and extended by the package's pauses.
/// A package can be deleted only before its first delivery day.
/// </summary>
public sealed class CustomerPackageService(IFitzobitDbContext db, DictionaryService dictionaries, DeliveryScheduleService schedule, IAuditLogger audit, IClock clock)
{
    internal sealed record ValidatedPackage(DictionaryEntry Preference, DictionaryEntry MealType, int Quantity, DictionaryEntry Frequency, DateOnly StartDate,
        DateOnly EndDate);

    public async Task<PackageSavedDto> AddAsync(int customerId, SavePackageRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new NotFoundException($"Customer {customerId} was not found.");

        var v = await ValidateAsync(request, null, ct);
        var package = NewPackage(v);
        customer.Packages.Add(package);
        audit.RecordChange(AuditActions.CustomerPackageAdded, nameof(Customer), customer.CustomerId,
            $"Package added: {customer.CustomerId} {Describe(v)}", new Dictionary<string, object?>(), Snapshot(v));
        await db.SaveChangesAsync(ct);
        return new PackageSavedDto(package.Id, package.StartDate, package.EndDate, package.PausedCount);
    }

    public async Task<PackageSavedDto> UpdateAsync(int packageId, UpdatePackageRequest request, CancellationToken ct = default)
    {
        var package = await db.CustomerPackages.Include(p => p.Customer).Include(p => p.Pauses).Include(p => p.Preference).Include(p => p.MealType)
                          .Include(p => p.Frequency).FirstOrDefaultAsync(p => p.Id == packageId, ct)
                      ?? throw new NotFoundException($"Package {packageId} was not found.");
        if (Math.Abs((package.UpdatedAt - DateTime.SpecifyKind(request.ExpectedUpdatedAt, DateTimeKind.Utc)).TotalMilliseconds) > 1)
            throw new BusinessRuleException($"This package was changed by {package.UpdatedBy ?? "another user"} after you opened it. Reload and try again.");

        var v = await ValidateAsync(request, package, ct);
        var scheduleChanged = v.StartDate != package.StartDate || v.Frequency.Id != package.FrequencyId;
        var endDate = package.EndDate;
        var pausedCount = package.PausedCount;
        if (scheduleChanged)
        {
            if (!request.ConfirmScheduleChange)
                throw new BusinessRuleException("Changing the start date or frequency recalculates the end date. Confirm the change to continue.");
            var pausedDates = package.Pauses.Select(p => p.PausedDate).ToList();
            endDate = schedule.CalculateEndDate(v.StartDate, v.Frequency, pausedDates);
            // Only pauses inside the new period count towards its limit.
            pausedCount = pausedDates.Count(d => d >= v.StartDate && d <= endDate);
            if (pausedCount > CustomerPackage.MaxPauses)
                throw new BusinessRuleException($"The new dates would include {pausedCount} paused days; a package allows {CustomerPackage.MaxPauses}.");
        }

        var changes = new ChangeSet();
        changes.Track("Preference", package.Preference!.DisplayName, v.Preference.DisplayName);
        changes.Track("MealType", package.MealType!.DisplayName, v.MealType.DisplayName);
        changes.Track("Quantity", package.Quantity, v.Quantity);
        changes.Track("Frequency", package.Frequency!.DisplayName, v.Frequency.DisplayName);
        changes.Track("StartDate", package.StartDate, v.StartDate);
        changes.Track("EndDate", package.EndDate, endDate);
        changes.Track("PausedCount", package.PausedCount, pausedCount);
        if (!changes.HasChanges) return new PackageSavedDto(package.Id, package.StartDate, package.EndDate, package.PausedCount);

        package.PreferenceId = v.Preference.Id;
        package.MealTypeId = v.MealType.Id;
        package.Quantity = v.Quantity;
        package.FrequencyId = v.Frequency.Id;
        package.StartDate = v.StartDate;
        package.EndDate = endDate;
        package.PausedCount = pausedCount;
        audit.RecordChange(AuditActions.CustomerPackageUpdated, nameof(Customer), package.Customer!.CustomerId,
            $"Package updated: {package.Customer.CustomerId} {Describe(v with { EndDate = endDate })} ({string.Join(", ", changes.New.Keys)})",
            changes.Old, changes.New);
        await db.SaveChangesAsync(ct);
        return new PackageSavedDto(package.Id, package.StartDate, package.EndDate, package.PausedCount);
    }

    /// <summary>Removes a package entered by mistake. Only possible before its first delivery day (start date after today).</summary>
    public async Task DeleteAsync(int packageId, CancellationToken ct = default)
    {
        var package = await db.CustomerPackages.Include(p => p.Customer).Include(p => p.Preference)
                          .Include(p => p.MealType).Include(p => p.Frequency).FirstOrDefaultAsync(p => p.Id == packageId, ct)
                      ?? throw new NotFoundException($"Package {packageId} was not found.");
        if (!CanDelete(package, DeliveryScheduleService.Today(clock)))
            throw new BusinessRuleException(
                $"This package started on {package.StartDate:dd MMM yyyy}; only packages whose first delivery day has not come yet can be deleted.");
        var description = Describe(new ValidatedPackage(package.Preference!, package.MealType!, package.Quantity, package.Frequency!, package.StartDate,
            package.EndDate));
        db.CustomerPackages.Remove(package);
        audit.RecordChange(AuditActions.CustomerPackageDeleted, nameof(Customer), package.Customer!.CustomerId,
            $"Package deleted: {package.Customer!.CustomerId} {description}",
            new Dictionary<string, object?> { ["Package"] = description }, new Dictionary<string, object?>());
        await db.SaveChangesAsync(ct);
    }

    public static bool CanDelete(CustomerPackage package, DateOnly today) => package.StartDate > today;

    /// <param name="existing">The package being edited (null when adding). Values it already has stay valid even if since deactivated,
    /// and an unchanged start date is accepted as stored.</param>
    internal async Task<ValidatedPackage> ValidateAsync(SavePackageRequest r, CustomerPackage? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (r.PreferenceId is null) errors.Add("Preference is required.");
        if (r.MealTypeId is null) errors.Add("Meal type is required.");
        if (r.FrequencyId is null) errors.Add("Frequency is required.");
        if (r.StartDate is null) errors.Add("Start date is required.");
        var quantity = r.Quantity ?? 1;
        if (quantity is < 1 or > CustomerPackage.MaxQuantity) errors.Add($"Quantity must be between 1 and {CustomerPackage.MaxQuantity}.");
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        var start = r.StartDate!.Value;
        if (start != existing?.StartDate) schedule.ValidateStartDate(start);
        var preference = await dictionaries.ResolveSelectionAsync(DictionaryType.Preference, r.PreferenceId!.Value, existing?.PreferenceId, ct);
        var mealType = await dictionaries.ResolveSelectionAsync(DictionaryType.MealType, r.MealTypeId!.Value, existing?.MealTypeId, ct);
        var frequency = await dictionaries.ResolveSelectionAsync(DictionaryType.Frequency, r.FrequencyId!.Value, existing?.FrequencyId, ct);
        return new ValidatedPackage(preference, mealType, quantity, frequency, start, schedule.CalculateEndDate(start, frequency, []));
    }

    internal static CustomerPackage NewPackage(ValidatedPackage v) => new()
    {
        PreferenceId = v.Preference.Id,
        MealTypeId = v.MealType.Id,
        Quantity = v.Quantity,
        FrequencyId = v.Frequency.Id,
        StartDate = v.StartDate,
        EndDate = v.EndDate,
        PausedCount = 0
    };

    /// <summary>"Veg Medium ×2 · Monthly (12 Oct 2026 – 10 Nov 2026)"</summary>
    internal static string Describe(ValidatedPackage v) =>
        Text(v.Preference.DisplayName, v.MealType.DisplayName, v.Quantity, v.Frequency.DisplayName, v.StartDate, v.EndDate);

    public static string Text(string preference, string mealType, int quantity, string frequency, DateOnly start, DateOnly end) =>
        $"{preference} {mealType} ×{quantity} · {frequency} ({start:dd MMM yyyy} – {end:dd MMM yyyy})";

    private static Dictionary<string, object?> Snapshot(ValidatedPackage v) => new()
    {
        ["Preference"] = v.Preference.DisplayName,
        ["MealType"] = v.MealType.DisplayName,
        ["Quantity"] = v.Quantity,
        ["Frequency"] = v.Frequency.DisplayName,
        ["StartDate"] = v.StartDate,
        ["EndDate"] = v.EndDate
    };
}
