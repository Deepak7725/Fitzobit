using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.DeliveryGroups;

public sealed record AssignmentResult(int CustomerId, int? PreviousGroupId, int NewGroupId, bool Changed);

public sealed record BulkAssignmentResult(int Requested, int Changed, int AlreadyInGroup);

/// <param name="Assigned">Customers that had no group and got one.</param>
/// <param name="Moved">Customers moved from one group to another.</param>
/// <param name="Removed">Customers taken out of their group.</param>
public sealed record GroupChangeResult(int Requested, int Assigned, int Moved, int Removed, int Unchanged)
{
    public int Changed => Assigned + Moved + Removed;
}

/// <summary>
/// Enforces "a customer has at most one active delivery group". Every change deactivates the previous assignment and
/// records history (previous group, new group, who, when, reason) and audit in the same save, so a batch is all-or-nothing.
/// The database also guards the rule with a filtered unique index.
/// </summary>
public sealed class DeliveryGroupAssignmentService(IFitzobitDbContext db, ICurrentUser currentUser, IClock clock, IAuditLogger audit)
{
    public async Task<AssignmentResult> AssignAsync(int customerId, int deliveryGroupId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
            throw new NotFoundException($"Customer {customerId} was not found.");

        var previous = await db.CustomerDeliveryGroupAssignments
            .Where(a => a.CustomerId == customerId && a.IsActive).Select(a => (int?)a.DeliveryGroupId).FirstOrDefaultAsync(cancellationToken);
        var result = await AssignManyAsync([customerId], deliveryGroupId, reason, cancellationToken);
        return new AssignmentResult(customerId, previous, deliveryGroupId, result.Changed > 0);
    }

    /// <summary>Moves/assigns many customers to one group in a single save. Customers already in the group are left alone.</summary>
    public async Task<BulkAssignmentResult> AssignManyAsync(IReadOnlyCollection<int> customerIds, int deliveryGroupId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");
        var result = await ApplyAsync(ids.ToDictionary(id => id, _ => (int?)deliveryGroupId), reason, cancellationToken);
        return new BulkAssignmentResult(result.Requested, result.Changed, result.Unchanged);
    }

    /// <summary>Removes customers from their current delivery group (history and audit are recorded).</summary>
    public async Task<BulkAssignmentResult> UnassignManyAsync(IReadOnlyCollection<int> customerIds, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");
        var result = await ApplyAsync(ids.ToDictionary(id => id, _ => (int?)null), reason, cancellationToken);
        return new BulkAssignmentResult(result.Requested, result.Changed, result.Unchanged);
    }

    /// <summary>
    /// Applies a target group (a group id, or null for "no group") per customer in one save. Unchanged customers are skipped;
    /// target groups must exist and be active.
    /// </summary>
    public async Task<GroupChangeResult> ApplyAsync(IReadOnlyDictionary<int, int?> targetGroupByCustomer, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        if (targetGroupByCustomer.Count == 0) throw new BusinessRuleException("Select at least one customer.");
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > 500) throw new BusinessRuleException("The reason must be at most 500 characters.");

        var ids = targetGroupByCustomer.Keys.ToList();
        var customers = await db.Customers.Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.CustomerId }).ToListAsync(ct);
        if (customers.Count != ids.Count) throw new NotFoundException("One or more customers were not found.");

        var targetIds = targetGroupByCustomer.Values.Where(v => v != null).Select(v => v!.Value).Distinct().ToList();
        var targets = await db.DeliveryGroups.Where(g => targetIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        foreach (var id in targetIds)
        {
            if (!targets.TryGetValue(id, out var group)) throw new NotFoundException($"Delivery group {id} was not found.");
            if (group.Status != RecordStatus.Active)
                throw new BusinessRuleException($"{DeliveryGroup.Label(group.GroupNumber)} is inactive and cannot receive customers.");
        }

        var active = await db.CustomerDeliveryGroupAssignments.Where(a => ids.Contains(a.CustomerId) && a.IsActive).ToListAsync(ct);
        var activeByCustomer = active.GroupBy(a => a.CustomerId).ToDictionary(g => g.Key, g => g.ToList());
        var previousIds = active.Select(a => a.DeliveryGroupId).Distinct().ToList();
        var numbers = await db.DeliveryGroups.Where(g => previousIds.Contains(g.Id) || targetIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.GroupNumber, ct);

        var now = clock.UtcNow;
        int assigned = 0, moved = 0, removed = 0;
        foreach (var customer in customers.OrderBy(c => c.Id))
        {
            var current = activeByCustomer.GetValueOrDefault(customer.Id) ?? [];
            var previousGroupId = current.FirstOrDefault()?.DeliveryGroupId;
            var targetGroupId = targetGroupByCustomer[customer.Id];
            if (current.Count <= 1 && previousGroupId == targetGroupId) continue;

            foreach (var assignment in current) assignment.IsActive = false;
            if (targetGroupId is { } newGroupId)
                db.CustomerDeliveryGroupAssignments.Add(new CustomerDeliveryGroupAssignment
                {
                    CustomerId = customer.Id, DeliveryGroupId = newGroupId, AssignedAt = now, AssignedBy = currentUser.UserName, IsActive = true
                });
            db.CustomerDeliveryGroupAssignmentHistory.Add(new CustomerDeliveryGroupAssignmentHistory
            {
                CustomerId = customer.Id, PreviousGroupId = previousGroupId, NewGroupId = targetGroupId,
                ChangedAt = now, ChangedBy = currentUser.UserName, Reason = reason
            });

            int? previousNumber = previousGroupId is { } p ? numbers[p] : null;
            int? newNumber = targetGroupId is { } t ? numbers[t] : null;
            var (action, summary) = (previousNumber, newNumber) switch
            {
                (null, { } n) => (AuditActions.CustomerAssigned, $"{customer.CustomerId} assigned to {DeliveryGroup.Label(n)}"),
                ({ } from, { } to) => (AuditActions.CustomerReassigned, $"{customer.CustomerId} moved from {DeliveryGroup.Label(from)} to {DeliveryGroup.Label(to)}"),
                ({ } from, null) => (AuditActions.CustomerUnassigned, $"{customer.CustomerId} removed from {DeliveryGroup.Label(from)}"),
                _ => (AuditActions.CustomerUnassigned, $"{customer.CustomerId}: duplicate group assignment cleared")
            };
            audit.Record(action, nameof(Customer), customer.CustomerId, summary,
                new { PreviousGroupNumber = previousNumber, NewGroupNumber = newNumber, Reason = reason });

            if (previousNumber is null) assigned++;
            else if (newNumber is null) removed++;
            else moved++;
        }

        if (assigned + moved + removed > 0) await db.SaveChangesAsync(ct);
        return new GroupChangeResult(customers.Count, assigned, moved, removed, customers.Count - assigned - moved - removed);
    }
}
