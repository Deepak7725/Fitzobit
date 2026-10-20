using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

public sealed class PauseCustomerRequest
{
    /// <summary>The delivery days the customer will not take (any order; duplicates are ignored).</summary>
    public List<DateOnly> Dates { get; set; } = [];

    public string? Reason { get; set; }
}

/// <param name="ExtendedEndDate">Whether this paused day moved the package's end date (only its first paused days do).</param>
public sealed record PausedDayDto(DateOnly Date, bool ExtendedEndDate);

/// <param name="ExtendingPausesLeft">Paused days this package can still add to its end date.</param>
public sealed record PausedPackageDto(int PackageId, string Package, DateOnly PreviousEndDate, DateOnly NewEndDate, IReadOnlyList<PausedDayDto> Days,
    int PausedCount, int ExtendingPausesLeft);

/// <param name="NotPaused">Packages running on the chosen days whose frequency cannot pause; they are still delivered.</param>
public sealed record PauseResultDto(IReadOnlyList<DateOnly> Dates, IReadOnlyList<PausedPackageDto> Packages, IReadOnlyList<string> NotPaused);

public sealed record PackagePauseDto(DateOnly PausedDate, DateOnly PreviousEndDate, DateOnly NewEndDate, bool ExtendedEndDate, string? Reason,
    DateTime CreatedAt, string? CreatedBy);

/// <summary>
/// Pauses one or more delivery days of a customer. Only packages whose frequency allows pausing (<see cref="DictionaryEntry.CanPause"/>,
/// e.g. Monthly) are paused; other packages running those days (e.g. Weekly, Trail) are still delivered. Each package's first
/// <see cref="CustomerPackage.MaxPauses"/> paused days move its end date to the next delivery day (skipping Sundays), so those meals
/// are not lost; further paused days are skipped and the end date stays. Only Active or Paused customers, only today or later. Everything
/// is saved together in one transaction; package row versions and the unique (package, date) index stop a repeated or concurrent
/// request from counting twice. <see cref="PreviewAsync"/> runs the same rules without saving.
/// </summary>
public sealed class CustomerPauseService(IFitzobitDbContext db, DeliveryScheduleService schedule, IAuditLogger audit, ICurrentUser currentUser, IClock clock,
    CustomerAutomationService automation)
{
    public const int MaxReasonLength = 300;

    /// <summary>What <see cref="PauseAsync"/> would do, without saving anything.</summary>
    public Task<PauseResultDto> PreviewAsync(int customerId, PauseCustomerRequest request, CancellationToken ct = default) =>
        PlanAsync(customerId, request, save: false, ct);

    public Task<PauseResultDto> PauseAsync(int customerId, PauseCustomerRequest request, CancellationToken ct = default) =>
        PlanAsync(customerId, request, save: true, ct);

    private async Task<PauseResultDto> PlanAsync(int customerId, PauseCustomerRequest request, bool save, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == customerId)
                           .Select(c => new { c.Id, c.CustomerId, StatusCode = c.Status!.Code, Status = c.Status.DisplayName }).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException($"Customer {customerId} was not found.");
        if (customer.StatusCode is not (CustomerStatusCodes.Active or CustomerStatusCodes.Paused))
            throw new BusinessRuleException(
                $"{customer.CustomerId} is {customer.Status}. Only active or paused customers can pause a delivery; change the status to Active first.");

        var dates = (request.Dates ?? []).Distinct().Order().ToList();
        if (dates.Count == 0) throw new BusinessRuleException("Choose at least one day to pause.");
        var calendar = schedule.Calendar;
        var today = DeliveryScheduleService.Today(clock);
        var sundays = dates.Where(d => !calendar.IsDeliveryDay(d)).ToList();
        if (sundays.Count > 0) throw new BusinessRuleException($"{Days(sundays)} {(sundays.Count == 1 ? "is a Sunday" : "are Sundays")}, not delivery days.");
        if (dates[0] < today) throw new BusinessRuleException("Only today or future delivery days can be paused.");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason?.Length > MaxReasonLength) throw new BusinessRuleException($"The reason must be at most {MaxReasonLength} characters.");

        var query = db.CustomerPackages.Include(p => p.Pauses).Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency)
            .Where(p => p.CustomerId == customerId && p.EndDate >= today && p.ArchivedAt == null);
        var packages = await (save ? query : query.AsNoTracking()).OrderBy(p => p.StartDate).ThenBy(p => p.Id).ToListAsync(ct);
        var notPausable = packages.Where(p => !p.Frequency!.CanPause && dates.Any(p.Covers)).Select(Label).ToList();

        // Days are processed in order, so a day added to the end by an earlier pause can itself be paused in the same request.
        var paused = new Dictionary<CustomerPackage, (DateOnly PreviousEnd, List<PausedDayDto> Days)>();
        foreach (var date in dates)
        {
            var running = packages.Where(p => p.Frequency!.CanPause && p.Covers(date)).ToList();
            if (running.Count == 0)
            {
                var others = packages.Where(p => p.Covers(date)).ToList();
                throw new BusinessRuleException(others.Count == 0
                    ? $"{customer.CustomerId} has no package delivering on {date:ddd dd MMM yyyy}."
                    : $"{date:ddd dd MMM yyyy} cannot be paused: {string.Join("; ", others.Select(Label))} cannot be paused (only packages whose " +
                      "frequency allows pausing, see Settings → Dropdown values).");
            }
            var toPause = running.Where(p => p.Pauses.All(x => x.PausedDate != date)).ToList();
            if (toPause.Count == 0) throw new BusinessRuleException($"{date:ddd dd MMM yyyy} is already paused.");

            foreach (var package in toPause)
            {
                var previousEnd = package.EndDate;
                var extend = package.PausedCount < CustomerPackage.MaxPauses;
                var newEnd = extend ? calendar.ExtendEndDateForPause(previousEnd) : previousEnd;
                package.Pauses.Add(new PackagePause
                {
                    PausedDate = date,
                    PreviousEndDate = previousEnd,
                    NewEndDate = newEnd,
                    Reason = reason,
                    CreatedAt = clock.UtcNow,
                    CreatedBy = currentUser.UserName
                });
                if (extend)
                {
                    package.PausedCount++;
                    package.EndDate = newEnd;
                }
                if (!paused.TryGetValue(package, out var entry)) paused[package] = entry = (previousEnd, []);
                entry.Days.Add(new PausedDayDto(date, extend));
            }
        }

        var results = paused.Select(x => new PausedPackageDto(x.Key.Id, Label(x.Key), x.Value.PreviousEnd, x.Key.EndDate, x.Value.Days,
            x.Key.PausedCount, CustomerPackage.MaxPauses - x.Key.PausedCount)).ToList();
        if (!save) return new PauseResultDto(dates, results, notPausable);

        audit.RecordChange(AuditActions.CustomerPaused, nameof(Customer), customer.CustomerId,
            $"Delivery paused: {customer.CustomerId} on {Days(dates)} ({results.Count} package(s))",
            results.ToDictionary(r => $"#{r.PackageId} {r.Package}", r => (object?)new { EndDate = r.PreviousEndDate }),
            results.ToDictionary(r => $"#{r.PackageId} {r.Package}", r => (object?)new { EndDate = r.NewEndDate, r.PausedCount,
                PausedDays = r.Days.Select(d => d.Date).ToList() }),
            new { PausedDates = dates, Reason = reason, NotPaused = notPausable });
        await db.SaveChangesAsync(ct);
        await automation.ApplyNowAsync(customerId, ct); // after 12 pm, e.g. Paused at once when tomorrow is paused
        return new PauseResultDto(dates, results, notPausable);
    }

    private static string Days(IEnumerable<DateOnly> dates) => string.Join(", ", dates.Select(d => d.ToString("dd MMM yyyy")));

    /// <summary>"Veg Medium ×1 · Monthly (12 Oct 2026 – 10 Nov 2026)"</summary>
    private static string Label(CustomerPackage p) =>
        CustomerPackageService.Text(p.Preference!.DisplayName, p.MealType!.DisplayName, p.Quantity, p.Frequency!.DisplayName, p.StartDate, p.EndDate);
}
