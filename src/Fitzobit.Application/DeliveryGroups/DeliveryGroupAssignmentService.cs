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

/// <summary>
/// Enforces "a customer has at most one active delivery group". Reassigning deactivates the previous
/// assignment and records history + audit in the same save. (The database also guards the rule with a
/// filtered unique index.)
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

        var group = await db.DeliveryGroups.FirstOrDefaultAsync(g => g.Id == deliveryGroupId, cancellationToken)
                    ?? throw new NotFoundException($"Delivery group {deliveryGroupId} was not found.");
        if (group.Status != RecordStatus.Active)
            throw new BusinessRuleException($"Delivery group \"{group.Name}\" is inactive and cannot receive customers.");

        var customers = await db.Customers.Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.CustomerId }).ToListAsync(cancellationToken);
        if (customers.Count != ids.Count) throw new NotFoundException("One or more customers were not found.");

        var active = await db.CustomerDeliveryGroupAssignments
            .Where(a => ids.Contains(a.CustomerId) && a.IsActive)
            .ToListAsync(cancellationToken);
        var activeByCustomer = active.GroupBy(a => a.CustomerId).ToDictionary(g => g.Key, g => g.ToList());

        var now = clock.UtcNow;
        var changed = 0;
        foreach (var customer in customers)
        {
            var current = activeByCustomer.GetValueOrDefault(customer.Id) ?? [];
            if (current.Count == 1 && current[0].DeliveryGroupId == deliveryGroupId) continue;

            var previousGroupId = current.FirstOrDefault()?.DeliveryGroupId;
            foreach (var assignment in current) assignment.IsActive = false;

            db.CustomerDeliveryGroupAssignments.Add(new CustomerDeliveryGroupAssignment
            {
                CustomerId = customer.Id, DeliveryGroupId = deliveryGroupId, AssignedAt = now, AssignedBy = currentUser.UserName, IsActive = true
            });
            db.CustomerDeliveryGroupAssignmentHistory.Add(new CustomerDeliveryGroupAssignmentHistory
            {
                CustomerId = customer.Id, PreviousGroupId = previousGroupId, NewGroupId = deliveryGroupId,
                ChangedAt = now, ChangedBy = currentUser.UserName, Reason = reason
            });

            var reassigned = previousGroupId is not null;
            audit.Record(reassigned ? AuditActions.CustomerReassigned : AuditActions.CustomerAssigned,
                nameof(Customer), customer.CustomerId,
                reassigned
                    ? $"{customer.CustomerId} moved to delivery group \"{group.Name}\""
                    : $"{customer.CustomerId} assigned to delivery group \"{group.Name}\"",
                new { PreviousGroupId = previousGroupId, NewGroupId = deliveryGroupId, Reason = reason });
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync(cancellationToken);
        return new BulkAssignmentResult(ids.Count, changed, ids.Count - changed);
    }

    /// <summary>Removes customers from their current delivery group (history and audit are recorded).</summary>
    public async Task<BulkAssignmentResult> UnassignManyAsync(IReadOnlyCollection<int> customerIds, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var ids = customerIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException("Select at least one customer.");

        var active = await db.CustomerDeliveryGroupAssignments
            .Where(a => ids.Contains(a.CustomerId) && a.IsActive)
            .Select(a => new { Assignment = a, a.Customer!.CustomerId, GroupName = a.DeliveryGroup!.Name })
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;
        foreach (var x in active)
        {
            x.Assignment.IsActive = false;
            db.CustomerDeliveryGroupAssignmentHistory.Add(new CustomerDeliveryGroupAssignmentHistory
            {
                CustomerId = x.Assignment.CustomerId, PreviousGroupId = x.Assignment.DeliveryGroupId, NewGroupId = null,
                ChangedAt = now, ChangedBy = currentUser.UserName, Reason = reason
            });
            audit.Record(AuditActions.CustomerUnassigned, nameof(Customer), x.CustomerId,
                $"{x.CustomerId} removed from delivery group \"{x.GroupName}\"",
                new { PreviousGroupId = x.Assignment.DeliveryGroupId, Reason = reason });
        }

        if (active.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return new BulkAssignmentResult(ids.Count, active.Count, ids.Count - active.Count);
    }
}
