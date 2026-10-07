using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Imports;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.DeliveryGroups;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.DeliveryGroups;

public sealed record GroupCustomerDto(
    int Id,
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? Location,
    string? Pincode,
    string? Locality,
    string? City,
    string? PlanName,
    DateTime? AssignedAt);

public sealed record MovePreviewDto(int Selected, int WillMove, int AlreadyInTarget, string TargetName, IReadOnlyList<string> Messages);

/// <summary>Manages saved delivery groups: create, rename, deactivate/activate, delete, list members and move customers.</summary>
public sealed class DeliveryGroupService(IFitzobitDbContext db, IAuditLogger audit, DeliveryGroupAssignmentService assignments)
{
    public const int MaxNameLength = 150;
    public const string Unassigned = "unassigned";

    public async Task<DeliveryGroup> CreateAsync(string name, string? description, CancellationToken ct = default)
    {
        var clean = await ValidateNameAsync(name, null, ct);
        var group = new DeliveryGroup { Name = clean, Description = Clean(description), Status = RecordStatus.Active };
        db.DeliveryGroups.Add(group);
        audit.Record(AuditActions.GroupCreated, nameof(DeliveryGroup), clean, $"Delivery group created: {clean}");
        await db.SaveChangesAsync(ct);
        return group;
    }

    public async Task UpdateAsync(int id, string name, string? description, CancellationToken ct = default)
    {
        var group = await FindAsync(id, ct);
        var clean = await ValidateNameAsync(name, id, ct);
        var renamed = group.Name != clean;
        var old = group.Name;
        group.Name = clean;
        group.Description = Clean(description);
        audit.Record(AuditActions.GroupUpdated, nameof(DeliveryGroup), clean,
            renamed ? $"Delivery group renamed: {old} → {clean}" : $"Delivery group updated: {clean}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deactivates a group. Its customers must be moved first so nobody is left in an inactive group.</summary>
    public async Task SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        var group = await FindAsync(id, ct);
        if (!active)
        {
            var members = await db.CustomerDeliveryGroupAssignments.CountAsync(a => a.DeliveryGroupId == id && a.IsActive, ct);
            if (members > 0)
                throw new BusinessRuleException($"\"{group.Name}\" still has {members} customer(s). Move them to another group before deactivating it.");
        }
        group.Status = active ? RecordStatus.Active : RecordStatus.Inactive;
        audit.Record(AuditActions.GroupUpdated, nameof(DeliveryGroup), group.Name,
            active ? $"Delivery group reactivated: {group.Name}" : $"Delivery group deactivated: {group.Name}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a group that has never had customers. Groups with history can only be deactivated.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var group = await FindAsync(id, ct);
        var used = await db.CustomerDeliveryGroupAssignments.AnyAsync(a => a.DeliveryGroupId == id, ct)
                   || await db.CustomerDeliveryGroupAssignmentHistory.AnyAsync(h => h.NewGroupId == id || h.PreviousGroupId == id, ct)
                   || await db.ImportRows.AnyAsync(r => r.TargetDeliveryGroupId == id && r.ImportBatch!.Status == Domain.Imports.ImportBatchStatus.Validated, ct);
        if (used)
            throw new BusinessRuleException($"\"{group.Name}\" has assignment history or is used by an import in progress. Deactivate it instead.");

        db.DeliveryGroups.Remove(group);
        audit.Record(AuditActions.GroupUpdated, nameof(DeliveryGroup), group.Name, $"Delivery group deleted: {group.Name}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Active members of a group, or customers without a group when <paramref name="groupKey"/> is "unassigned".</summary>
    public async Task<PagedResult<GroupCustomerDto>> GetCustomersAsync(string groupKey, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLower();

        if (groupKey == Unassigned)
        {
            var customers = db.Customers.AsNoTracking().Where(c => !c.GroupAssignments.Any(a => a.IsActive));
            if (s is not null)
                customers = customers.Where(c => c.CustomerId.ToLower().Contains(s) || c.CustomerName.ToLower().Contains(s)
                                                 || c.PhoneNumber.Contains(s) || (c.Location != null && c.Location.ToLower().Contains(s))
                                                 || (c.Pincode != null && c.Pincode.Contains(s)));

            var total = await customers.CountAsync(ct);
            var items = await customers.OrderBy(c => c.CustomerId).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new GroupCustomerDto(c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.Location, c.Pincode, c.Locality, c.City,
                    c.Plan != null ? c.Plan.Name : null, null))
                .ToListAsync(ct);
            return new PagedResult<GroupCustomerDto>(items, total, page, pageSize);
        }

        if (!int.TryParse(groupKey, out var groupId)) throw new BusinessRuleException($"\"{groupKey}\" is not a valid group.");
        await FindAsync(groupId, ct);

        // Driven from the group's active assignments (indexed by group) rather than testing every customer.
        var members = db.CustomerDeliveryGroupAssignments.AsNoTracking().Where(a => a.DeliveryGroupId == groupId && a.IsActive);
        if (s is not null)
            members = members.Where(a => a.Customer!.CustomerId.ToLower().Contains(s) || a.Customer.CustomerName.ToLower().Contains(s)
                                         || a.Customer.PhoneNumber.Contains(s) || (a.Customer.Location != null && a.Customer.Location.ToLower().Contains(s))
                                         || (a.Customer.Pincode != null && a.Customer.Pincode.Contains(s)));

        var memberTotal = await members.CountAsync(ct);
        var memberItems = await members.OrderBy(a => a.Customer!.CustomerId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new GroupCustomerDto(a.Customer!.Id, a.Customer.CustomerId, a.Customer.CustomerName, a.Customer.PhoneNumber,
                a.Customer.Location, a.Customer.Pincode, a.Customer.Locality, a.Customer.City,
                a.Customer.Plan != null ? a.Customer.Plan.Name : null, a.AssignedAt))
            .ToListAsync(ct);
        return new PagedResult<GroupCustomerDto>(memberItems, memberTotal, page, pageSize);
    }

    public async Task<MovePreviewDto> PreviewMoveAsync(IReadOnlyCollection<int> customerIds, int targetGroupId, CancellationToken ct = default)
    {
        var target = await FindAsync(targetGroupId, ct);
        if (target.Status != RecordStatus.Active) throw new BusinessRuleException($"\"{target.Name}\" is inactive.");
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");

        var current = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroupId).FirstOrDefault())
            .ToListAsync(ct);
        var groupNames = await db.DeliveryGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var same = current.Count(g => g == targetGroupId);
        var messages = current.Where(g => g != targetGroupId).GroupBy(g => g).OrderByDescending(g => g.Count())
            .Select(g => g.Key is { } from
                ? $"{Customers(g.Count())} will be moved from {groupNames[from]} to {target.Name}."
                : $"{Customers(g.Count())} without a group will be assigned to {target.Name}.")
            .ToList();
        if (same > 0) messages.Add($"{Customers(same)} already in {target.Name} (no change).");
        return new MovePreviewDto(current.Count, current.Count - same, same, target.Name, messages);
    }

    /// <summary>What removing the selected customers from their delivery group would change.</summary>
    public async Task<MovePreviewDto> PreviewUnassignAsync(IReadOnlyCollection<int> customerIds, CancellationToken ct = default)
    {
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");

        var current = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => c.GroupAssignments.Where(a => a.IsActive).Select(a => a.DeliveryGroup!.Name).FirstOrDefault())
            .ToListAsync(ct);

        var messages = current.Where(g => g is not null).GroupBy(g => g!).OrderByDescending(g => g.Count())
            .Select(g => $"{Customers(g.Count())} will be removed from {g.Key}.")
            .ToList();
        var without = current.Count(g => g is null);
        if (without > 0) messages.Add($"{Customers(without)} already without a group (no change).");
        return new MovePreviewDto(current.Count, current.Count - without, without, "No group", messages);
    }

    public Task<BulkAssignmentResult> MoveAsync(IReadOnlyCollection<int> customerIds, int targetGroupId, string? reason, CancellationToken ct = default) =>
        assignments.AssignManyAsync(customerIds, targetGroupId, string.IsNullOrWhiteSpace(reason) ? "Moved by manager" : reason.Trim(), ct);

    private async Task<DeliveryGroup> FindAsync(int id, CancellationToken ct) =>
        await db.DeliveryGroups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException($"Delivery group {id} was not found.");

    private async Task<string> ValidateNameAsync(string name, int? exceptId, CancellationToken ct)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) throw new BusinessRuleException("Group name is required.");
        if (clean.Length > MaxNameLength) throw new BusinessRuleException($"Group name must be at most {MaxNameLength} characters.");
        var lower = clean.ToLower();
        if (await db.DeliveryGroups.AnyAsync(g => g.Id != exceptId && g.Name.ToLower() == lower, ct))
            throw new BusinessRuleException($"A delivery group named \"{clean}\" already exists.");
        return clean;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Customers(int count) => count == 1 ? "1 customer" : $"{count:N0} customers";
}
