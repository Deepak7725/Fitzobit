using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>What one 12 pm run changed (for the log and tests).</summary>
public sealed record AutomationRunResult(DateOnly Day, int PackagesMovedToPast, int MadeInactive, int MadeActive, int MadePaused)
{
    public bool ChangedAnything => PackagesMovedToPast + MadeInactive + MadeActive + MadePaused > 0;
}

/// <summary>
/// The daily 12 pm run (India time), owner's rules of 2026-10-20. For each day <c>D</c> it looks ahead to the next delivery day
/// <c>N</c> (Saturday looks at Monday):
/// <list type="number">
/// <item>Packages whose last day is <c>D</c> (or earlier) move to the customer's past packages.</item>
/// <item>A customer left with no current package (running or starting later) becomes Inactive.</item>
/// <item>A customer whose package starts on <c>N</c> becomes Active if Inactive or Paused.</item>
/// <item>An Active customer with nothing delivered on <c>N</c> because every package running then is paused becomes Paused; a Paused
/// customer whose paused days end with <c>D</c> (the last delivery day up to <c>D</c> was fully paused) and who gets a delivery on
/// <c>N</c> becomes Active again. A Weekly/Trail package still delivering keeps the customer Active.</item>
/// </list>
/// The rules only act at these moments, so a status changed by hand is otherwise left alone. Missed days (server asleep) are run in
/// order when it wakes (<see cref="RunDueAsync"/>). A pause or package saved later than the run that decided its day applies the rules
/// to that customer straight away (<see cref="ApplyNowAsync"/>), e.g. a pause for today saved in the morning. Every change is audited.
/// </summary>
public sealed class CustomerAutomationService(IFitzobitDbContext db, IDeliveryCalendarService calendar, IAuditLogger audit, IClock clock)
{
    /// <summary>AppSettings key of the last day whose 12 pm run is complete ("yyyy-MM-dd").</summary>
    public const string LastRunKey = "Automation.LastRunDate";

    public static readonly TimeOnly RunTime = new(12, 0);

    private const string RunLabel = "12 pm run";

    private DateTime NowInIndia => clock.UtcNow.AddMinutes(330);

    /// <summary>The latest day whose 12 pm run is due: today from 12 pm (India time), otherwise yesterday.</summary>
    public DateOnly DueDay()
    {
        var now = NowInIndia;
        var today = DateOnly.FromDateTime(now);
        return TimeOnly.FromDateTime(now) >= RunTime ? today : today.AddDays(-1);
    }

    /// <summary>Runs every day that is due and not run yet, oldest first, each in its own transaction. The very first run only runs the due day.</summary>
    public async Task<IReadOnlyList<AutomationRunResult>> RunDueAsync(CancellationToken ct = default)
    {
        var due = DueDay();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == LastRunKey, ct);
        DateOnly? last = DateOnly.TryParseExact(setting?.Value, "yyyy-MM-dd", out var parsed) ? parsed : null;
        if (last >= due) return [];

        var results = new List<AutomationRunResult>();
        for (var day = last?.AddDays(1) ?? due; day <= due; day = day.AddDays(1))
        {
            results.Add(await RunDayAsync(day, null, ct));
            if (setting is null) db.AppSettings.Add(setting = new AppSetting { Key = LastRunKey });
            setting.Value = day.ToString("yyyy-MM-dd");
            await db.SaveChangesAsync(ct);
        }
        return results;
    }

    /// <summary>
    /// Re-applies the latest due run (<see cref="DueDay"/>) to one customer after a pause or package was saved. After 12 pm that is
    /// today's run (tomorrow paused → Paused at once); before 12 pm it is yesterday's run, which looks at today (owner's decision,
    /// 2026-10-26: a pause for today saved in the morning → Paused at once). A pause for tomorrow saved in the morning still waits for
    /// today's 12 pm run.
    /// </summary>
    public async Task ApplyNowAsync(int customerId, CancellationToken ct = default)
    {
        var result = await RunDayAsync(DueDay(), customerId, ct);
        if (result.ChangedAnything) await db.SaveChangesAsync(ct);
    }

    /// <summary>Applies the rules for day <paramref name="day"/> (to every customer, or one); the caller saves.</summary>
    private async Task<AutomationRunResult> RunDayAsync(DateOnly day, int? customerId, CancellationToken ct)
    {
        var next = calendar.NextDeliveryDay(day);
        var previous = day;
        while (!calendar.IsDeliveryDay(previous)) previous = previous.AddDays(-1);

        var statuses = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == DictionaryType.CustomerStatus)
            .ToDictionaryAsync(e => e.Id, e => (e.Code, e.DisplayName), ct);
        int StatusId(string code) => statuses.First(s => s.Value.Code == code).Key;

        // 1. Packages that finished move to the past packages.
        var ending = await db.CustomerPackages.Include(p => p.Customer).Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency)
            .Where(p => p.ArchivedAt == null && p.EndDate <= day && (customerId == null || p.CustomerId == customerId))
            .ToListAsync(ct);
        foreach (var p in ending)
        {
            p.ArchivedAt = clock.UtcNow;
            var label = CustomerPackageService.Text(p.Preference!.DisplayName, p.MealType!.DisplayName, p.Quantity, p.Frequency!.DisplayName, p.StartDate,
                p.EndDate);
            audit.RecordChange(AuditActions.CustomerPackageEnded, nameof(Customer), p.Customer!.CustomerId,
                $"Package finished: {p.Customer.CustomerId} {label} (moved to past packages by the {RunLabel})",
                new Dictionary<string, object?> { ["Package"] = label }, new Dictionary<string, object?> { ["Package"] = $"{label} (past)" });
        }
        var ended = ending.Select(p => p.CustomerId).ToHashSet();

        // 2–4. Statuses, for customers that just lost a package or still have a current one.
        var customers = await db.Customers
            .Include(c => c.Packages.Where(p => p.ArchivedAt == null)).ThenInclude(p => p.Pauses)
            .Where(c => customerId == null ? c.Packages.Any(p => p.ArchivedAt == null) || ended.Contains(c.Id) : c.Id == customerId)
            .ToListAsync(ct);

        int inactive = 0, active = 0, paused = 0;
        foreach (var c in customers)
        {
            var current = c.Packages.Where(p => !p.IsPast).ToList();
            var code = statuses[c.StatusId].Code;
            (string Code, string Reason)? change = null;

            if (current.Count == 0)
            {
                if (ended.Contains(c.Id) && code != CustomerStatusCodes.Inactive)
                    change = (CustomerStatusCodes.Inactive, $"its last package finished on {LastEnd(ending, c.Id):dd MMM yyyy}");
            }
            else if (code != CustomerStatusCodes.Active && current.Any(p => p.StartDate > day && p.StartDate <= next) && Delivers(current, next))
            {
                change = (CustomerStatusCodes.Active, $"a package starts on {next:ddd dd MMM yyyy}");
            }
            else if (code == CustomerStatusCodes.Active && FullyPaused(current, next))
            {
                change = (CustomerStatusCodes.Paused, $"every package is paused on {next:ddd dd MMM yyyy}");
            }
            else if (code == CustomerStatusCodes.Paused && FullyPaused(current, previous) && Delivers(current, next))
            {
                change = (CustomerStatusCodes.Active, $"the paused days are over; deliveries resume on {next:ddd dd MMM yyyy}");
            }

            if (change is not { } to) continue;
            var oldName = statuses[c.StatusId].DisplayName;
            c.StatusId = StatusId(to.Code);
            var newName = statuses[c.StatusId].DisplayName;
            audit.RecordChange(AuditActions.CustomerStatusChangedAutomatically, nameof(Customer), c.CustomerId,
                $"Status {oldName} → {newName}: {c.CustomerId}, {to.Reason} ({RunLabel})",
                new Dictionary<string, object?> { ["Status"] = oldName }, new Dictionary<string, object?> { ["Status"] = newName });
            switch (to.Code)
            {
                case CustomerStatusCodes.Inactive: inactive++; break;
                case CustomerStatusCodes.Paused: paused++; break;
                default: active++; break;
            }
        }
        return new AutomationRunResult(day, ending.Count, inactive, active, paused);
    }

    private static DateOnly LastEnd(IEnumerable<CustomerPackage> ending, int customerId) => ending.Where(p => p.CustomerId == customerId).Max(p => p.EndDate);

    private static bool IsPaused(CustomerPackage p, DateOnly date) => p.Pauses.Any(x => x.PausedDate == date);

    /// <summary>At least one package runs on <paramref name="date"/> and every one of them is paused that day.</summary>
    private static bool FullyPaused(IReadOnlyList<CustomerPackage> packages, DateOnly date)
    {
        var running = packages.Where(p => p.Covers(date)).ToList();
        return running.Count > 0 && running.All(p => IsPaused(p, date));
    }

    /// <summary>Some package delivers on <paramref name="date"/> (runs and is not paused).</summary>
    private bool Delivers(IReadOnlyList<CustomerPackage> packages, DateOnly date) =>
        calendar.IsDeliveryDay(date) && packages.Any(p => p.Covers(date) && !IsPaused(p, date));
}
