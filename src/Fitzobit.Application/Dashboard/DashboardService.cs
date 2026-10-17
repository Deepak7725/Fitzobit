using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Dashboard;

public sealed record ChartItemDto(string Key, string Label, int Count);

/// <summary>Customers per status; <see cref="Code"/> is the stable dictionary code (ACTIVE, PAUSED, INACTIVE, …).</summary>
public sealed record StatusCountDto(int Id, string Code, string Name, int Count);

/// <param name="CustomerKey">The internal id of the customer (null if it no longer exists), for opening it.</param>
public sealed record RecentChangeDto(long Id, DateTime OccurredAt, string Action, string? CustomerId, int? CustomerKey, string Summary, string? PerformedBy);

public sealed record DashboardSummaryDto(
    int TotalCustomers,
    int TotalPackages,
    int ActiveCustomers,
    int PausedCustomers,
    int InactiveCustomers,
    int TotalDeliveryGroups,
    int UnassignedCustomers,
    int UnassignedActiveCustomers,
    int WithCoordinates,
    int WithoutCoordinates,
    int ApproximateCoordinates,
    int EndingSoon,
    int EndingSoonWindowDays,
    int IncompleteCustomers,
    IReadOnlyList<StatusCountDto> CustomersByStatus,
    IReadOnlyList<ChartItemDto> CustomersByGroup,
    IReadOnlyList<ChartItemDto> CustomersByMealType,
    IReadOnlyList<ChartItemDto> CustomersByFrequency,
    IReadOnlyList<ChartItemDto> CustomersByPincode,
    IReadOnlyList<ChartItemDto> CustomersByCity,
    IReadOnlyList<RecentChangeDto> RecentChanges);

/// <summary>Live dashboard figures, all counted in PostgreSQL. Statuses are matched by their stable dictionary codes.</summary>
public sealed class DashboardService(IFitzobitDbContext db, IClock clock)
{
    /// <summary>"Ending soon": packages of active customers whose last delivery day is within this many days (today included).</summary>
    public const int EndingSoonWindowDays = 7;
    public const int RecentChangeCount = 8;
    private const int ChartTop = 8;

    private static readonly string[] CustomerActions =
    [
        AuditActions.CustomerCreated, AuditActions.CustomerUpdated, AuditActions.CustomerPaused, AuditActions.CustomerAssigned,
        AuditActions.CustomerReassigned, AuditActions.CustomerUnassigned, AuditActions.CustomerPackageAdded, AuditActions.CustomerPackageUpdated,
        AuditActions.CustomerPackageDeleted, AuditActions.CustomerNoteAdded, AuditActions.CustomerNoteUpdated, AuditActions.CustomerNoteDeleted
    ];

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var today = DeliveryScheduleService.Today(clock);
        var soon = today.AddDays(EndingSoonWindowDays - 1);
        var customers = db.Customers.AsNoTracking();
        var unassigned = customers.Where(c => !c.GroupAssignments.Any(a => a.IsActive));

        var statuses = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == DictionaryType.CustomerStatus)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName)
            .Select(e => new StatusCountDto(e.Id, e.Code, e.DisplayName, customers.Count(c => c.StatusId == e.Id)))
            .ToListAsync(ct);
        int Status(string code) => statuses.Where(s => s.Code == code).Sum(s => s.Count);

        var unassignedCount = await unassigned.CountAsync(ct);
        var byGroup = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .GroupBy(a => new { a.DeliveryGroupId, a.DeliveryGroup!.GroupNumber })
            .Select(g => new { g.Key.DeliveryGroupId, g.Key.GroupNumber, Count = g.Count() })
            .ToListAsync(ct);
        var groupChart = byGroup.OrderBy(g => g.GroupNumber)
            .Select(g => new ChartItemDto(g.DeliveryGroupId.ToString(), Domain.DeliveryGroups.DeliveryGroup.Label(g.GroupNumber), g.Count)).ToList();
        if (unassignedCount > 0) groupChart.Add(new ChartItemDto("none", "No group", unassignedCount));

        // Meal types and frequencies count packages (a customer with two weekly packages counts twice).
        var packages = db.CustomerPackages.AsNoTracking();
        var byMealType = await packages.GroupBy(p => new { p.MealTypeId, p.MealType!.DisplayName })
            .Select(g => new { g.Key.MealTypeId, g.Key.DisplayName, Count = g.Count() }).ToListAsync(ct);
        var byFrequency = await packages.GroupBy(p => new { p.FrequencyId, p.Frequency!.DisplayName })
            .Select(g => new { g.Key.FrequencyId, g.Key.DisplayName, Count = g.Count() }).ToListAsync(ct);

        var withCoordinates = await customers.CountAsync(c => c.Latitude != null && c.Longitude != null, ct);
        var total = await customers.CountAsync(ct);

        return new DashboardSummaryDto(
            TotalCustomers: total,
            TotalPackages: await packages.CountAsync(ct),
            ActiveCustomers: Status(CustomerStatusCodes.Active),
            PausedCustomers: Status(CustomerStatusCodes.Paused),
            InactiveCustomers: Status(CustomerStatusCodes.Inactive),
            TotalDeliveryGroups: await db.DeliveryGroups.CountAsync(g => g.Status == RecordStatus.Active, ct),
            UnassignedCustomers: unassignedCount,
            UnassignedActiveCustomers: await unassigned.CountAsync(c => c.Status!.Code == CustomerStatusCodes.Active, ct),
            WithCoordinates: withCoordinates,
            WithoutCoordinates: total - withCoordinates,
            ApproximateCoordinates: await customers.CountAsync(c => c.Latitude != null && c.GeocodeMatchLevel == GeoMatchLevel.Pincode, ct),
            EndingSoon: await packages.CountAsync(p => p.Customer!.Status!.Code == CustomerStatusCodes.Active && p.EndDate >= today && p.EndDate <= soon, ct),
            EndingSoonWindowDays: EndingSoonWindowDays,
            IncompleteCustomers: await customers.CountAsync(c => !c.Packages.Any() || c.PreferredDeliveryTime == null || c.Pincode == null, ct),
            CustomersByStatus: statuses,
            CustomersByGroup: groupChart,
            CustomersByMealType: Chart(byMealType.Select(x => ((int?)x.MealTypeId, (string?)x.DisplayName, x.Count))),
            CustomersByFrequency: Chart(byFrequency.Select(x => ((int?)x.FrequencyId, (string?)x.DisplayName, x.Count))),
            CustomersByPincode: await TopAsync(customers.Where(c => c.Pincode != null).GroupBy(c => c.Pincode!), ct),
            CustomersByCity: await TopAsync(customers.Where(c => c.City != null).GroupBy(c => c.City!), ct),
            RecentChanges: await RecentChangesAsync(ct));
    }

    /// <summary>The latest customer changes from the audit log (created, edited, paused, group changes).</summary>
    private async Task<List<RecentChangeDto>> RecentChangesAsync(CancellationToken ct)
    {
        var entries = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Customer) && CustomerActions.Contains(a.Action))
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(RecentChangeCount)
            .Select(a => new { a.Id, a.OccurredAt, a.Action, a.EntityKey, a.Summary, a.PerformedBy })
            .ToListAsync(ct);
        var keys = entries.Where(e => e.EntityKey != null).Select(e => e.EntityKey!).Distinct().ToList();
        var ids = await db.Customers.AsNoTracking().Where(c => keys.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.Id, ct);
        return entries.Select(e => new RecentChangeDto(e.Id, e.OccurredAt, e.Action, e.EntityKey,
            e.EntityKey != null && ids.TryGetValue(e.EntityKey, out var id) ? id : null, e.Summary, e.PerformedBy)).ToList();
    }

    /// <summary>Counts per drop-down value, largest first; customers without a value appear as "Not set".</summary>
    private static List<ChartItemDto> Chart(IEnumerable<(int? Id, string? Name, int Count)> rows) =>
        rows.Select(r => new ChartItemDto(r.Id?.ToString() ?? "none", r.Name ?? "Not set", r.Count))
            .OrderByDescending(i => i.Count).ThenBy(i => i.Label).ToList();

    private static async Task<List<ChartItemDto>> TopAsync(IQueryable<IGrouping<string, Customer>> groups, CancellationToken ct)
    {
        var items = await groups.Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Key).Take(ChartTop).ToListAsync(ct);
        return items.Select(x => new ChartItemDto(x.Key, x.Key, x.Count)).ToList();
    }
}
