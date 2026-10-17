using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
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
    string? Address,
    string? Pincode,
    string? Locality,
    string? City,
    string? MealType,
    string Status,
    bool HasLocation,
    DateTime? AssignedAt);

public sealed record MovePreviewDto(int Selected, int WillMove, int AlreadyInTarget, string TargetName, IReadOnlyList<string> Messages);

public sealed record CreatedGroupDto(int Id, int GroupNumber);

/// <summary>
/// Manages delivery groups, which users know by number ("Group 3"): create groups, notes, deactivate/activate, delete,
/// list members and move customers. Group numbers are allocated under a database lock, so two managers creating groups at
/// the same time never get the same number (a unique index guarantees it as well).
/// </summary>
public sealed class DeliveryGroupService(IFitzobitDbContext db, IAuditLogger audit, DeliveryGroupAssignmentService assignments)
{
    public const int MaxGroupsPerRequest = 50;
    public const int MaxNotesLength = 1000;
    public const string Unassigned = "unassigned";

    /// <summary>Lock id for allocating group numbers (PostgreSQL transaction-level advisory lock).</summary>
    private const long GroupNumberLock = 7_315_002;

    /// <summary>Creates <paramref name="count"/> new groups with the next free numbers.</summary>
    public async Task<IReadOnlyList<CreatedGroupDto>> CreateGroupsAsync(int count, string? notes = null, CancellationToken ct = default)
    {
        if (count is < 1 or > MaxGroupsPerRequest) throw new BusinessRuleException($"Create between 1 and {MaxGroupsPerRequest} groups at a time.");
        var cleanNotes = CleanNotes(notes);
        return await InLockAsync(async () =>
        {
            var next = (await db.DeliveryGroups.MaxAsync(g => (int?)g.GroupNumber, ct) ?? 0) + 1;
            return await AddAsync(Enumerable.Range(next, count).ToList(), cleanNotes, ct);
        }, ct);
    }

    /// <summary>
    /// Makes sure groups with these numbers exist (creating the missing ones) and returns number → group id. Existing
    /// groups must be active. Used when a reviewed classification is saved.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, int>> EnsureGroupsAsync(IReadOnlyCollection<int> numbers, CancellationToken ct = default)
    {
        var wanted = numbers.Distinct().ToList();
        if (wanted.Any(n => n < 1)) throw new BusinessRuleException("Group numbers start at 1.");
        if (wanted.Count == 0) return new Dictionary<int, int>();
        return await InLockAsync(async () =>
        {
            var existing = await db.DeliveryGroups.Where(g => wanted.Contains(g.GroupNumber)).ToListAsync(ct);
            var inactive = existing.FirstOrDefault(g => g.Status != RecordStatus.Active);
            if (inactive is not null) throw new BusinessRuleException($"{DeliveryGroup.Label(inactive.GroupNumber)} is inactive. Reactivate it or choose another group.");
            var created = await AddAsync(wanted.Except(existing.Select(g => g.GroupNumber)).Order().ToList(), null, ct);
            return existing.Select(g => (g.GroupNumber, g.Id)).Concat(created.Select(c => (c.GroupNumber, c.Id)))
                .ToDictionary(x => x.Item1, x => x.Item2);
        }, ct);
    }

    public async Task UpdateNotesAsync(int id, string? notes, CancellationToken ct = default)
    {
        var group = await FindAsync(id, ct);
        var clean = CleanNotes(notes);
        if (group.Description == clean) return;
        audit.RecordChange(AuditActions.GroupUpdated, nameof(DeliveryGroup), Label(group), $"{Label(group)} notes updated",
            new Dictionary<string, object?> { ["Notes"] = group.Description }, new Dictionary<string, object?> { ["Notes"] = clean });
        group.Description = clean;
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
                throw new BusinessRuleException($"{Label(group)} still has {members} customer(s). Move them to another group before deactivating it.");
        }
        group.Status = active ? RecordStatus.Active : RecordStatus.Inactive;
        audit.Record(AuditActions.GroupUpdated, nameof(DeliveryGroup), Label(group), active ? $"{Label(group)} reactivated" : $"{Label(group)} deactivated");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a group that has never had customers. Groups with history can only be deactivated.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var group = await FindAsync(id, ct);
        var used = await db.CustomerDeliveryGroupAssignments.AnyAsync(a => a.DeliveryGroupId == id, ct)
                   || await db.CustomerDeliveryGroupAssignmentHistory.AnyAsync(h => h.NewGroupId == id || h.PreviousGroupId == id, ct);
        if (used) throw new BusinessRuleException($"{Label(group)} has assignment history. Deactivate it instead.");

        db.DeliveryGroups.Remove(group);
        audit.Record(AuditActions.GroupUpdated, nameof(DeliveryGroup), Label(group), $"{Label(group)} deleted");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Active members of a group, or customers without a group when <paramref name="groupKey"/> is "unassigned".</summary>
    public async Task<PagedResult<GroupCustomerDto>> GetCustomersAsync(string groupKey, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLower();

        IQueryable<Domain.Customers.Customer> customers;
        if (groupKey == Unassigned) customers = db.Customers.AsNoTracking().Where(c => !c.GroupAssignments.Any(a => a.IsActive));
        else
        {
            if (!int.TryParse(groupKey, out var groupId)) throw new BusinessRuleException($"\"{groupKey}\" is not a valid group.");
            await FindAsync(groupId, ct);
            customers = db.Customers.AsNoTracking().Where(c => c.GroupAssignments.Any(a => a.IsActive && a.DeliveryGroupId == groupId));
        }
        if (s is not null)
            customers = customers.Where(c => c.CustomerId.ToLower().Contains(s) || c.CustomerName.ToLower().Contains(s) || c.PhoneNumber.Contains(s)
                                             || (c.Address != null && c.Address.ToLower().Contains(s)) || (c.Pincode != null && c.Pincode.Contains(s)));

        var total = await customers.CountAsync(ct);
        var found = await customers.OrderBy(c => c.CustomerId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.Address, c.Pincode, c.Locality, c.City,
                MealTypes = c.Packages.OrderBy(p => p.StartDate).Select(p => p.MealType!.DisplayName).ToList(),
                Status = c.Status!.DisplayName, Located = c.Latitude != null,
                AssignedAt = c.GroupAssignments.Where(a => a.IsActive).Select(a => (DateTime?)a.AssignedAt).FirstOrDefault()
            })
            .ToListAsync(ct);
        var items = found.Select(c => new GroupCustomerDto(c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.Address, c.Pincode, c.Locality, c.City,
            c.MealTypes.Count > 0 ? string.Join(", ", c.MealTypes.Distinct()) : null, c.Status, c.Located, c.AssignedAt)).ToList();
        return new PagedResult<GroupCustomerDto>(items, total, page, pageSize);
    }

    public async Task<MovePreviewDto> PreviewMoveAsync(IReadOnlyCollection<int> customerIds, int targetGroupId, CancellationToken ct = default)
    {
        var target = await FindAsync(targetGroupId, ct);
        if (target.Status != RecordStatus.Active) throw new BusinessRuleException($"{Label(target)} is inactive.");
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");

        var current = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroup!.GroupNumber).FirstOrDefault())
            .ToListAsync(ct);

        var same = current.Count(n => n == target.GroupNumber);
        var messages = current.Where(n => n != target.GroupNumber).GroupBy(n => n).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key is { } from
                ? $"{Customers(g.Count())} will be moved from {DeliveryGroup.Label(from)} to {Label(target)}."
                : $"{Customers(g.Count())} without a group will be assigned to {Label(target)}.")
            .ToList();
        if (same > 0) messages.Add($"{Customers(same)} already in {Label(target)} (no change).");
        return new MovePreviewDto(current.Count, current.Count - same, same, Label(target), messages);
    }

    /// <summary>What removing the selected customers from their delivery group would change.</summary>
    public async Task<MovePreviewDto> PreviewUnassignAsync(IReadOnlyCollection<int> customerIds, CancellationToken ct = default)
    {
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");

        var current = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroup!.GroupNumber).FirstOrDefault())
            .ToListAsync(ct);

        var messages = current.Where(n => n is not null).GroupBy(n => n!.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => $"{Customers(g.Count())} will be removed from {DeliveryGroup.Label(g.Key)}.")
            .ToList();
        var without = current.Count(n => n is null);
        if (without > 0) messages.Add($"{Customers(without)} already without a group (no change).");
        return new MovePreviewDto(current.Count, current.Count - without, without, "No group", messages);
    }

    public Task<BulkAssignmentResult> MoveAsync(IReadOnlyCollection<int> customerIds, int targetGroupId, string? reason, CancellationToken ct = default) =>
        assignments.AssignManyAsync(customerIds, targetGroupId, string.IsNullOrWhiteSpace(reason) ? "Moved by manager" : reason.Trim(), ct);

    private async Task<T> InLockAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        var owns = db.Database.CurrentTransaction is null;
        var transaction = owns ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({GroupNumberLock})", ct);
            var result = await work();
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task<List<CreatedGroupDto>> AddAsync(List<int> numbers, string? notes, CancellationToken ct)
    {
        if (numbers.Count == 0) return [];
        var groups = numbers.Select(n => new DeliveryGroup { GroupNumber = n, Description = notes, Status = RecordStatus.Active }).ToList();
        db.DeliveryGroups.AddRange(groups);
        audit.Record(AuditActions.GroupCreated, nameof(DeliveryGroup), groups.Count == 1 ? Label(groups[0]) : null,
            groups.Count == 1 ? $"{Label(groups[0])} created" : $"Groups {numbers.Min()}–{numbers.Max()} created ({groups.Count})",
            new { Numbers = numbers });
        await db.SaveChangesAsync(ct);
        return groups.Select(g => new CreatedGroupDto(g.Id, g.GroupNumber)).ToList();
    }

    private async Task<DeliveryGroup> FindAsync(int id, CancellationToken ct) =>
        await db.DeliveryGroups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException($"Delivery group {id} was not found.");

    private static string Label(DeliveryGroup group) => DeliveryGroup.Label(group.GroupNumber);

    private static string? CleanNotes(string? notes)
    {
        var clean = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (clean?.Length > MaxNotesLength) throw new BusinessRuleException($"Notes must be at most {MaxNotesLength} characters.");
        return clean;
    }

    private static string Customers(int count) => count == 1 ? "1 customer" : $"{count:N0} customers";
}
