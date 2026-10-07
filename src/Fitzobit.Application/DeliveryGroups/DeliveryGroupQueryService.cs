using Fitzobit.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.DeliveryGroups;

public sealed record DeliveryGroupDto(
    int Id,
    string Name,
    string? Description,
    string Status,
    int ActiveCustomerCount,
    int LocationCount,
    int PincodeCount,
    IReadOnlyList<string> Pincodes,
    IReadOnlyList<string> Localities,
    int PlanCount,
    IReadOnlyList<string> Plans,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime UpdatedAt);

public sealed record DeliveryGroupOverviewDto(IReadOnlyList<DeliveryGroupDto> Groups, int UnassignedCustomers, int TotalCustomers);

public sealed class DeliveryGroupQueryService(IFitzobitDbContext db)
{
    public async Task<IReadOnlyList<DeliveryGroupDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var groups = await db.DeliveryGroups.AsNoTracking()
            .OrderBy(g => g.Status).ThenBy(g => g.Name)
            .ToListAsync(cancellationToken);

        var members = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new { a.DeliveryGroupId, a.Customer!.NormalizedLocation, a.Customer.Pincode, a.Customer.Locality, PlanName = a.Customer.Plan != null ? a.Customer.Plan.Name : null })
            .ToListAsync(cancellationToken);
        var byGroup = members.ToLookup(m => m.DeliveryGroupId);

        return groups.Select(g =>
        {
            var m = byGroup[g.Id].ToList();
            var pins = m.Where(x => x.Pincode != null).Select(x => x.Pincode!).Distinct().Order().ToList();
            var localities = m.Where(x => x.Locality != null).Select(x => x.Locality!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            var plans = m.Where(x => x.PlanName != null).Select(x => x.PlanName!).Distinct().Order().ToList();
            return new DeliveryGroupDto(g.Id, g.Name, g.Description, g.Status.ToString(), m.Count,
                m.Where(x => x.NormalizedLocation != null).Select(x => x.NormalizedLocation).Distinct().Count(),
                pins.Count, pins.Take(12).ToList(), localities.Take(8).ToList(), plans.Count, plans.Take(6).ToList(),
                g.CreatedAt, g.CreatedBy, g.UpdatedAt);
        }).ToList();
    }

    public async Task<DeliveryGroupOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        new(await ListAsync(cancellationToken),
            await db.Customers.CountAsync(c => !c.GroupAssignments.Any(a => a.IsActive), cancellationToken),
            await db.Customers.CountAsync(cancellationToken));
}
