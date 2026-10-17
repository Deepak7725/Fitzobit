using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.DeliveryGroups;

public sealed record CountDto(string Label, int Count);

/// <param name="StatusCounts">Members by customer status (Active / Inactive / Paused …).</param>
/// <param name="LocatedCount">Members with known coordinates.</param>
public sealed record DeliveryGroupDto(
    int Id,
    int GroupNumber,
    string Name,
    string? Notes,
    string Status,
    int ActiveCustomerCount,
    int LocationCount,
    int PincodeCount,
    IReadOnlyList<string> Pincodes,
    IReadOnlyList<string> Localities,
    IReadOnlyList<CountDto> MealTypes,
    IReadOnlyList<CountDto> Frequencies,
    IReadOnlyList<CountDto> StatusCounts,
    int LocatedCount,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime UpdatedAt);

public sealed record DeliveryGroupOverviewDto(IReadOnlyList<DeliveryGroupDto> Groups, int UnassignedCustomers, int TotalCustomers);

public sealed class DeliveryGroupQueryService(IFitzobitDbContext db)
{
    public async Task<IReadOnlyList<DeliveryGroupDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var groups = await db.DeliveryGroups.AsNoTracking().OrderBy(g => g.GroupNumber).ToListAsync(ct);

        var members = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new
            {
                a.DeliveryGroupId,
                a.Customer!.NormalizedAddress,
                a.Customer.Pincode,
                a.Customer.Locality,
                MealTypes = a.Customer.Packages.Select(p => p.MealType!.DisplayName).ToList(),
                Frequencies = a.Customer.Packages.Select(p => p.Frequency!.DisplayName).ToList(),
                Status = a.Customer.Status!.DisplayName,
                StatusOrder = a.Customer.Status.SortOrder,
                Located = a.Customer.Latitude != null
            })
            .ToListAsync(ct);
        var byGroup = members.ToLookup(m => m.DeliveryGroupId);

        // Meal types and frequencies are counted per package (a customer with two packages counts twice).
        static List<CountDto> Counts(IEnumerable<string?> values) =>
            values.GroupBy(v => v ?? "Not set").Select(g => new CountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Label).ToList();

        return groups.Select(g =>
        {
            var m = byGroup[g.Id].ToList();
            var pins = m.Where(x => x.Pincode != null).Select(x => x.Pincode!).Distinct().Order().ToList();
            var localities = m.Where(x => x.Locality != null).Select(x => x.Locality!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            var statuses = m.GroupBy(x => (x.Status, x.StatusOrder)).OrderBy(x => x.Key.StatusOrder)
                .Select(x => new CountDto(x.Key.Status, x.Count())).ToList();
            return new DeliveryGroupDto(g.Id, g.GroupNumber, Domain.DeliveryGroups.DeliveryGroup.Label(g.GroupNumber), g.Description, g.Status.ToString(),
                m.Count, m.Where(x => x.NormalizedAddress != null).Select(x => x.NormalizedAddress).Distinct().Count(),
                pins.Count, pins.Take(12).ToList(), localities.Take(8).ToList(), Counts(m.SelectMany(x => x.MealTypes)), Counts(m.SelectMany(x => x.Frequencies)),
                statuses, m.Count(x => x.Located), g.CreatedAt, g.CreatedBy, g.UpdatedAt);
        }).ToList();
    }

    public async Task<DeliveryGroupOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        new(await ListAsync(cancellationToken),
            await db.Customers.CountAsync(c => !c.GroupAssignments.Any(a => a.IsActive), cancellationToken),
            await db.Customers.CountAsync(cancellationToken));
}
