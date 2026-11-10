using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>The fields of a package entered on the Add Customer screen or the package dialog. A new package's end date is always calculated.</summary>
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

    /// <summary>
    /// The end date typed in the package dialog (a delivery day on or after the start date). Null, or the same as the calculated
    /// end date, keeps the calculation.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>The package price in rupees (all boxes, all deliveries), from a Manager only; ignored for other users. Null = no price.</summary>
    public decimal? Price { get; set; }
}

public sealed record PackageSavedDto(int Id, DateOnly StartDate, DateOnly EndDate, int PausedCount);

/// <summary>
/// A customer's packages: add, edit and delete. Each package has its own preference, meal type, quantity, frequency and dates; the
/// end date is calculated in delivery days (<see cref="DeliveryScheduleService"/>) and extended by every paused day; when editing it
/// can also be set by hand (later pauses still extend it). A package can be deleted only before its
/// first delivery day.
/// </summary>
public sealed class CustomerPackageService(IFitzobitDbContext db, DictionaryService dictionaries, DeliveryScheduleService schedule, IAuditLogger audit, IClock clock,
    CustomerAutomationService automation, ICurrentUser currentUser)
{
    internal sealed record ValidatedPackage(DictionaryEntry Preference, DictionaryEntry MealType, int Quantity, DictionaryEntry Frequency, DateOnly StartDate,
        DateOnly EndDate);

    public async Task<PackageSavedDto> AddAsync(int customerId, SavePackageRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new NotFoundException($"Customer {customerId} was not found.");

        var v = await ValidateAsync(request, null, ct);
        var package = NewPackage(v);
        package.Price = await Money.MoneyService.ListPriceAsync(db, v.MealType.Id, v.Preference.Id, v.Frequency.Id, v.Quantity, ct);
        customer.Packages.Add(package);
        await ApplyFestiveDaysAsync(package, ct);
        audit.RecordChange(AuditActions.CustomerPackageAdded, nameof(Customer), customer.CustomerId,
            $"Package added: {customer.CustomerId} {Describe(v)}", new Dictionary<string, object?>(), Snapshot(v));
        await db.SaveChangesAsync(ct);
        await automation.ApplyNowAsync(customerId, packageSaved: true, ct); // e.g. Active at once when it delivers next
        return new PackageSavedDto(package.Id, package.StartDate, package.EndDate, package.PausedCount);
    }

    public async Task<PackageSavedDto> UpdateAsync(int packageId, UpdatePackageRequest request, CancellationToken ct = default)
    {
        var package = await db.CustomerPackages.Include(p => p.Customer).Include(p => p.Pauses).Include(p => p.Preference).Include(p => p.MealType)
                          .Include(p => p.Frequency).FirstOrDefaultAsync(p => p.Id == packageId, ct)
                      ?? throw new NotFoundException($"Package {packageId} was not found.");
        if (Math.Abs((package.UpdatedAt - DateTime.SpecifyKind(request.ExpectedUpdatedAt, DateTimeKind.Utc)).TotalMilliseconds) > 1)
            throw new BusinessRuleException($"This package was changed by {package.UpdatedBy ?? "another user"} after you opened it. Reload and try again.");
        if (package.IsPast) throw new BusinessRuleException(PastPackageMessage);

        var v = await ValidateAsync(request, package, ct);
        var scheduleChanged = v.StartDate != package.StartDate || v.Frequency.Id != package.FrequencyId;
        var endDate = package.EndDate;
        var pausedCount = package.PausedCount;
        if (scheduleChanged)
        {
            if (!request.ConfirmScheduleChange)
                throw new BusinessRuleException("Changing the start date or frequency recalculates the end date. Confirm the change to continue.");
            // Every paused day inside the new period extends it, the customer's own and those paused for everyone.
            var own = package.Pauses.Where(p => p.FestivePauseId == null).Select(p => p.PausedDate).Where(d => d >= v.StartDate).Order().ToList();
            var festive = package.Pauses.Where(p => p.FestivePauseId != null && p.PausedDate >= v.StartDate).Select(p => p.PausedDate).ToList();
            endDate = schedule.CalculateEndDate(v.StartDate, v.Frequency, [.. own, .. festive]);
            pausedCount = own.Count(d => d <= endDate);
            // Days paused for everyone that are no longer inside the package are dropped; new ones inside it are added below.
            foreach (var outside in package.Pauses.Where(p => p.FestivePauseId != null && (p.PausedDate < v.StartDate || p.PausedDate > endDate)).ToList())
            {
                package.Pauses.Remove(outside);
                db.PackagePauses.Remove(outside);
            }
        }
        var typedEndDate = request.EndDate is { } t && t != endDate;
        if (request.EndDate is { } typed && typed != endDate)
        {
            if (typed < v.StartDate) throw new BusinessRuleException("The end date cannot be before the start date.");
            if (!schedule.Calendar.IsDeliveryDay(typed))
                throw new BusinessRuleException($"{typed:dd MMM yyyy} is a Sunday. The end date must be a delivery day (Monday to Saturday).");
            endDate = typed;
        }

        // Price (Managers only see and set it): a Manager's value is taken as sent; for anyone else a different box, quantity or
        // frequency takes the list price, otherwise the price stays.
        var price = package.Price;
        if (Money.MoneyService.CanSeeMoney(currentUser))
        {
            if (request.Price is < 0 or > Domain.Money.PackagePrice.MaxPrice * CustomerPackage.MaxQuantity)
                throw new BusinessRuleException($"The price must be between ₹0 and ₹{Domain.Money.PackagePrice.MaxPrice * CustomerPackage.MaxQuantity:N0}.");
            price = request.Price is { } p ? Math.Round(p, 2) : null;
        }
        else if (v.MealType.Id != package.MealTypeId || v.Preference.Id != package.PreferenceId || v.Frequency.Id != package.FrequencyId
                 || v.Quantity != package.Quantity)
            price = await Money.MoneyService.ListPriceAsync(db, v.MealType.Id, v.Preference.Id, v.Frequency.Id, v.Quantity, ct);

        var datesChanged = scheduleChanged || endDate != package.EndDate;
        var changes = new ChangeSet();
        changes.Track("Preference", package.Preference!.DisplayName, v.Preference.DisplayName);
        changes.Track("MealType", package.MealType!.DisplayName, v.MealType.DisplayName);
        changes.Track("Quantity", package.Quantity, v.Quantity);
        changes.Track("Frequency", package.Frequency!.DisplayName, v.Frequency.DisplayName);
        changes.Track("StartDate", package.StartDate, v.StartDate);
        changes.Track("EndDate", package.EndDate, endDate);
        changes.Track("PausedCount", package.PausedCount, pausedCount);
        if (price != package.Price) changes.Track("Price", "(not shown)", "changed"); // amounts stay off the audit log (Admins read it)
        if (!changes.HasChanges) return new PackageSavedDto(package.Id, package.StartDate, package.EndDate, package.PausedCount);

        package.PreferenceId = v.Preference.Id;
        package.MealTypeId = v.MealType.Id;
        package.Quantity = v.Quantity;
        package.FrequencyId = v.Frequency.Id;
        package.StartDate = v.StartDate;
        package.EndDate = endDate;
        package.PausedCount = pausedCount;
        package.Price = price;
        if (scheduleChanged && !typedEndDate) await ApplyFestiveDaysAsync(package, ct); // a typed end date is kept as typed
        audit.RecordChange(AuditActions.CustomerPackageUpdated, nameof(Customer), package.Customer!.CustomerId,
            $"Package updated: {package.Customer.CustomerId} {Describe(v with { EndDate = endDate })} ({string.Join(", ", changes.New.Keys)})",
            changes.Old, changes.New);
        await db.SaveChangesAsync(ct);
        await automation.ApplyNowAsync(package.CustomerId, packageSaved: datesChanged, ct);
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

    public static bool CanDelete(CustomerPackage package, DateOnly today) => !package.IsPast && package.StartDate > today;

    /// <summary>Pauses the upcoming days paused for everyone that fall inside the package (each moves its end date one delivery day).</summary>
    internal async Task ApplyFestiveDaysAsync(CustomerPackage package, CancellationToken ct)
    {
        var today = DeliveryScheduleService.Today(clock);
        var festive = await db.FestivePauses.AsNoTracking().Where(f => f.Date >= today && f.Date >= package.StartDate).OrderBy(f => f.Date).ToListAsync(ct);
        ApplyFestiveDays(package, festive, schedule.Calendar);
    }

    /// <summary>Pauses the given days paused for everyone (in date order) that fall inside the package and are not paused yet; each moves
    /// its end date one delivery day, so a later one may come inside as well. Returns the days added.</summary>
    internal static List<DateOnly> ApplyFestiveDays(CustomerPackage package, IEnumerable<FestivePause> festive, IDeliveryCalendarService calendar)
    {
        var added = new List<DateOnly>();
        foreach (var f in festive.OrderBy(f => f.Date))
        {
            if (!package.Covers(f.Date) || package.Pauses.Any(x => x.PausedDate == f.Date)) continue;
            FestivePauseService.AddFestivePause(package, f, calendar.ExtendEndDateForPause(package.EndDate));
            added.Add(f.Date);
        }
        return added;
    }

    public const string PastPackageMessage = "This package has finished and is in the past packages, which are kept for history only. Use “Add package” for a renewal.";

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
