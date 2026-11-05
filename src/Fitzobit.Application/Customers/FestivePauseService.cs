using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

public sealed class FestivePauseRequest
{
    /// <summary>The delivery days to pause for everyone (any order; duplicates are ignored).</summary>
    public List<DateOnly> Dates { get; set; } = [];

    /// <summary>Shown on each paused day, e.g. "Diwali".</summary>
    public string? Reason { get; set; }
}

/// <summary>One package affected by a festive pause: its end date before and after, and the days paused.</summary>
public sealed record FestivePackageDto(int CustomerId, string CustomerCode, string CustomerName, string Package, DateOnly PreviousEndDate,
    DateOnly NewEndDate, IReadOnlyList<DateOnly> Days);

public sealed record FestivePreviewDto(IReadOnlyList<DateOnly> Dates, int Customers, int Packages, IReadOnlyList<FestivePackageDto> Items);

/// <param name="CanRemove">The day has not come yet (removing it moves the end dates back).</param>
public sealed record FestivePauseDto(int Id, DateOnly Date, string? Reason, int Customers, int Packages, DateTime CreatedAt, string? CreatedBy, bool CanRemove);

/// <summary>
/// Pause for everyone (owner's decisions, 2026-11-04): on festive or emergency days chosen by an administrator, every package running
/// that day — any frequency — of Active and Paused customers is paused. These pauses always move the end date one delivery day (no
/// meal lost) and do not count towards the package's own <see cref="CustomerPackage.MaxPauses"/>; a day the customer had already
/// paused is left as it is. Packages saved later over such a day get the pause too (<see cref="CustomerPackageService"/>). A festive
/// pause can be removed before its day, which moves every affected end date back. The 12 pm status rules apply as for any pause.
/// </summary>
public sealed class FestivePauseService(IFitzobitDbContext db, DeliveryScheduleService schedule, IAuditLogger audit, ICurrentUser currentUser, IClock clock,
    CustomerAutomationService automation)
{
    public const int MaxReasonLength = 300;

    public async Task<IReadOnlyList<FestivePauseDto>> ListAsync(CancellationToken ct = default)
    {
        var today = DeliveryScheduleService.Today(clock);
        var rows = await db.FestivePauses.AsNoTracking().OrderByDescending(f => f.Date)
            .Select(f => new
            {
                f.Id, f.Date, f.Reason, f.CreatedAt, f.CreatedBy,
                Packages = f.Pauses.Count(),
                Customers = f.Pauses.Select(p => p.Package!.CustomerId).Distinct().Count()
            })
            .ToListAsync(ct);
        return rows.Select(f => new FestivePauseDto(f.Id, f.Date, f.Reason, f.Customers, f.Packages, f.CreatedAt, f.CreatedBy, f.Date > today)).ToList();
    }

    public Task<FestivePreviewDto> PreviewAsync(FestivePauseRequest request, CancellationToken ct = default) => PlanAsync(request, save: false, ct);

    public Task<FestivePreviewDto> CreateAsync(FestivePauseRequest request, CancellationToken ct = default) => PlanAsync(request, save: true, ct);

    private async Task<FestivePreviewDto> PlanAsync(FestivePauseRequest request, bool save, CancellationToken ct)
    {
        var dates = (request.Dates ?? []).Distinct().Order().ToList();
        if (dates.Count == 0) throw new BusinessRuleException("Choose at least one day to pause for everyone.");
        var calendar = schedule.Calendar;
        var today = DeliveryScheduleService.Today(clock);
        var sundays = dates.Where(d => !calendar.IsDeliveryDay(d)).ToList();
        if (sundays.Count > 0) throw new BusinessRuleException($"{Days(sundays)} {(sundays.Count == 1 ? "is a Sunday" : "are Sundays")}, not delivery days.");
        if (dates[0] < today) throw new BusinessRuleException("Only today or future delivery days can be paused.");
        var existing = await db.FestivePauses.AsNoTracking().Where(f => dates.Contains(f.Date)).Select(f => f.Date).ToListAsync(ct);
        if (existing.Count > 0) throw new BusinessRuleException($"{Days(existing.Order())} {(existing.Count == 1 ? "is" : "are")} already paused for everyone.");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason?.Length > MaxReasonLength) throw new BusinessRuleException($"The reason must be at most {MaxReasonLength} characters.");

        var query = db.CustomerPackages.Include(p => p.Pauses).Include(p => p.Customer).Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency)
            .Where(p => p.ArchivedAt == null && p.EndDate >= dates[0]
                        && (p.Customer!.Status!.Code == CustomerStatusCodes.Active || p.Customer.Status.Code == CustomerStatusCodes.Paused));
        var packages = await (save ? query : query.AsNoTracking()).OrderBy(p => p.CustomerId).ThenBy(p => p.StartDate).ThenBy(p => p.Id).ToListAsync(ct);

        var affected = new Dictionary<CustomerPackage, (DateOnly PreviousEnd, List<DateOnly> Days)>();
        foreach (var date in dates)
        {
            var festive = new FestivePause { Date = date, Reason = reason, CreatedAt = clock.UtcNow, CreatedBy = currentUser.UserName };
            if (save) db.FestivePauses.Add(festive);
            // In date order, so a day that an earlier festive day moved into a package is paused as well.
            foreach (var package in packages.Where(p => p.Covers(date) && p.Pauses.All(x => x.PausedDate != date)))
            {
                var previousEnd = package.EndDate;
                AddFestivePause(package, festive, calendar.ExtendEndDateForPause(previousEnd));
                if (!affected.TryGetValue(package, out var entry)) affected[package] = entry = (previousEnd, []);
                entry.Days.Add(date);
            }
        }

        var items = affected.Select(a => new FestivePackageDto(a.Key.CustomerId, a.Key.Customer!.CustomerId, a.Key.Customer.CustomerName, Label(a.Key),
                a.Value.PreviousEnd, a.Key.EndDate, a.Value.Days))
            .OrderBy(i => i.CustomerCode).ToList();
        var result = new FestivePreviewDto(dates, items.Select(i => i.CustomerId).Distinct().Count(), items.Count, items);
        if (!save) return result;

        var label = $"{Days(dates)}{(reason is null ? "" : $" ({reason})")}";
        foreach (var customer in items.GroupBy(i => (i.CustomerId, i.CustomerCode)))
            audit.RecordChange(AuditActions.FestivePauseApplied, nameof(Customer), customer.Key.CustomerCode,
                $"Paused for everyone: {customer.Key.CustomerCode} on {label}",
                customer.ToDictionary(i => i.Package, i => (object?)new { EndDate = i.PreviousEndDate }),
                customer.ToDictionary(i => i.Package, i => (object?)new { EndDate = i.NewEndDate, PausedDays = i.Days }));
        audit.Record(AuditActions.FestivePauseApplied, nameof(FestivePause), string.Join(",", dates.Select(d => d.ToString("yyyy-MM-dd"))),
            $"Paused for everyone on {label}: {result.Customers} customer(s), {result.Packages} package(s)", new { Dates = dates, Reason = reason, result.Customers, result.Packages });
        await db.SaveChangesAsync(ct);
        foreach (var customerId in items.Select(i => i.CustomerId).Distinct()) await automation.ApplyNowAsync(customerId, ct: ct);
        return result;
    }

    /// <summary>Undoes a festive pause before its day: each affected package's end date moves back one delivery day.</summary>
    public async Task RemoveAsync(int id, CancellationToken ct = default)
    {
        var festive = await db.FestivePauses.Include(f => f.Pauses).ThenInclude(p => p.Package!).ThenInclude(p => p.Customer)
                          .FirstOrDefaultAsync(f => f.Id == id, ct)
                      ?? throw new NotFoundException($"Festive pause {id} was not found.");
        if (festive.Date <= DeliveryScheduleService.Today(clock))
            throw new BusinessRuleException($"{festive.Date:dd MMM yyyy} has already come; a pause for everyone can only be removed before its day.");

        var calendar = schedule.Calendar;
        var customers = new HashSet<(int Id, string Code)>();
        foreach (var pause in festive.Pauses.ToList())
        {
            var package = pause.Package!;
            if (pause.ExtendedEndDate)
            {
                var end = package.EndDate.AddDays(-1);
                while (!calendar.IsDeliveryDay(end)) end = end.AddDays(-1);
                package.EndDate = end < package.StartDate ? package.StartDate : end;
            }
            db.PackagePauses.Remove(pause);
            customers.Add((package.CustomerId, package.Customer!.CustomerId));
        }
        db.FestivePauses.Remove(festive);

        var label = $"{festive.Date:dd MMM yyyy}{(festive.Reason is null ? "" : $" ({festive.Reason})")}";
        foreach (var c in customers)
            audit.Record(AuditActions.FestivePauseRemoved, nameof(Customer), c.Code, $"Pause for everyone removed: {c.Code} on {label}", new { festive.Date });
        audit.Record(AuditActions.FestivePauseRemoved, nameof(FestivePause), festive.Date.ToString("yyyy-MM-dd"),
            $"Pause for everyone removed: {label} ({customers.Count} customer(s), {festive.Pauses.Count} package(s))", new { festive.Date, festive.Reason });
        await db.SaveChangesAsync(ct);

        // A customer switched to Paused because of this day gets Active again when the day was the next one to decide.
        if (calendar.NextDeliveryDay(automation.DueDay()) == festive.Date)
            foreach (var c in customers) await automation.ApplyNowAsync(c.Id, packageSaved: true, ct);
    }

    /// <summary>Adds a festive pause to a package and moves its end date.</summary>
    internal static void AddFestivePause(CustomerPackage package, FestivePause festive, DateOnly newEnd)
    {
        package.Pauses.Add(new PackagePause
        {
            PausedDate = festive.Date,
            PreviousEndDate = package.EndDate,
            NewEndDate = newEnd,
            Reason = festive.Reason ?? "Paused for everyone",
            // A saved festive pause is linked by id (it may not be tracked); a new one goes in with the package pause.
            FestivePause = festive.Id == 0 ? festive : null,
            FestivePauseId = festive.Id == 0 ? null : festive.Id,
            CreatedAt = festive.CreatedAt,
            CreatedBy = festive.CreatedBy
        });
        package.EndDate = newEnd;
    }

    private static string Days(IEnumerable<DateOnly> dates) => string.Join(", ", dates.Select(d => d.ToString("dd MMM yyyy")));

    private static string Label(CustomerPackage p) =>
        CustomerPackageService.Text(p.Preference!.DisplayName, p.MealType!.DisplayName, p.Quantity, p.Frequency!.DisplayName, p.StartDate, p.EndDate);
}
