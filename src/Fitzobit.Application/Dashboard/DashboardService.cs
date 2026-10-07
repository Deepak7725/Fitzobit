using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Catalogue;
using Fitzobit.Application.Imports;
using Fitzobit.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Dashboard;

public sealed record ChartItemDto(string Key, string Label, int Count);

public sealed record DashboardSummaryDto(
    int TotalCustomers,
    int TotalDeliveryGroups,
    int UniqueLocations,
    int UniquePincodes,
    int ActivePlans,
    int UnassignedCustomers,
    int RecentImports,
    int RecentImportWindowDays,
    int CatalogueItems,
    int CataloguePricingTiers,
    int CatalogueItemsWithWarnings,
    IReadOnlyList<ImportBatchDto> LatestImports,
    IReadOnlyList<CatalogueCategorySummaryDto> CatalogueCategories,
    IReadOnlyList<ChartItemDto> CustomersByGroup,
    IReadOnlyList<ChartItemDto> CustomersByPlan,
    IReadOnlyList<ChartItemDto> CustomersByPincode,
    IReadOnlyList<ChartItemDto> CustomersByCity);

public sealed class DashboardService(IFitzobitDbContext db, CatalogueService catalogue, ImportBatchQueryService imports, IClock clock)
{
    public const int RecentImportWindowDays = 30;
    private const int ChartTop = 8;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var recentSince = clock.UtcNow.AddDays(-RecentImportWindowDays);
        var overview = await catalogue.GetOverviewAsync(ct);
        var unassigned = await db.Customers.CountAsync(c => !c.GroupAssignments.Any(a => a.IsActive), ct);

        var byGroup = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .GroupBy(a => new { a.DeliveryGroupId, a.DeliveryGroup!.Name })
            .Select(g => new { g.Key.DeliveryGroupId, g.Key.Name, Count = g.Count() })
            .ToListAsync(ct);
        var groupChart = byGroup.OrderByDescending(g => g.Count).ThenBy(g => g.Name)
            .Select(g => new ChartItemDto(g.DeliveryGroupId.ToString(), g.Name, g.Count)).ToList();
        if (unassigned > 0) groupChart.Add(new ChartItemDto("none", "No group", unassigned));

        var byPlan = await db.Customers.AsNoTracking()
            .GroupBy(c => new { c.PlanId, Name = c.Plan != null ? c.Plan.Name : null })
            .Select(g => new { g.Key.PlanId, g.Key.Name, Count = g.Count() })
            .ToListAsync(ct);

        return new DashboardSummaryDto(
            TotalCustomers: await db.Customers.CountAsync(ct),
            TotalDeliveryGroups: await db.DeliveryGroups.CountAsync(g => g.Status == RecordStatus.Active, ct),
            UniqueLocations: await db.Customers.Where(c => c.NormalizedLocation != null)
                .Select(c => c.NormalizedLocation).Distinct().CountAsync(ct),
            UniquePincodes: await db.Customers.Where(c => c.Pincode != null)
                .Select(c => c.Pincode).Distinct().CountAsync(ct),
            ActivePlans: await db.Plans.CountAsync(p => p.Status == RecordStatus.Active, ct),
            UnassignedCustomers: unassigned,
            RecentImports: await db.ImportBatches.CountAsync(b => b.UploadedAt >= recentSince, ct),
            RecentImportWindowDays: RecentImportWindowDays,
            CatalogueItems: overview.TotalItems,
            CataloguePricingTiers: overview.TotalPricingTiers,
            CatalogueItemsWithWarnings: overview.ItemsWithWarnings,
            LatestImports: await imports.ListAsync(5, ct),
            CatalogueCategories: overview.Categories,
            CustomersByGroup: groupChart,
            CustomersByPlan: byPlan.OrderByDescending(p => p.Count)
                .Select(p => new ChartItemDto(p.PlanId?.ToString() ?? "none", p.Name ?? "No plan", p.Count)).ToList(),
            CustomersByPincode: await TopAsync(db.Customers.Where(c => c.Pincode != null).GroupBy(c => c.Pincode!), ct),
            CustomersByCity: await TopAsync(db.Customers.Where(c => c.City != null).GroupBy(c => c.City!), ct));
    }

    private static async Task<List<ChartItemDto>> TopAsync(IQueryable<IGrouping<string, Domain.Customers.Customer>> groups, CancellationToken ct)
    {
        var items = await groups.Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Key).Take(ChartTop).ToListAsync(ct);
        return items.Select(x => new ChartItemDto(x.Key, x.Key, x.Count)).ToList();
    }
}
