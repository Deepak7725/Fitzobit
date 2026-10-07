using System.Diagnostics;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Imports;
using Fitzobit.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fitzobit.Application.Imports;

/// <summary>
/// Step 7: Confirm &amp; Save. Writes groups, customers, assignments, assignment history, plan aliases, the batch
/// result and audit records inside one transaction. Any failure rolls everything back, so nothing is partially saved.
/// Existing customers' saved details are never overwritten; only their delivery group can change.
/// </summary>
public sealed class ImportCommitService(
    IFitzobitDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger audit,
    ILogger<ImportCommitService> logger)
{
    private const int ChunkSize = 1000;

    public async Task<ImportCommitResultDto> CommitAsync(int batchId, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw new NotFoundException($"Import {batchId} was not found.");
        if (batch.Status != ImportBatchStatus.Validated)
            throw new BusinessRuleException($"Import {batchId} is {batch.Status.ToString().ToLowerInvariant()} and cannot be saved.");

        // Read-only here: untracked, so later saves do not re-scan thousands of rows.
        var rows = await db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId).Included().OrderBy(r => r.RowNumber).ToListAsync(ct);
        if (rows.Count == 0) throw new BusinessRuleException("There are no rows to import.");
        var totalRows = await db.ImportRows.CountAsync(r => r.ImportBatchId == batchId, ct);
        var drafts = await db.ImportDraftGroups.Where(d => d.ImportBatchId == batchId).ToListAsync(ct);
        var manualMappings = await db.ImportPlanMappings
            .Where(m => m.ImportBatchId == batchId && m.Method == PlanMatchMethod.Manual && m.PlanId != null).ToListAsync(ct);

        await EnsureStillValidAsync(rows, drafts, ct);

        var newRows = rows.Where(r => !r.IsExistingCustomer).ToList();
        var existingRows = rows.Where(r => r.IsExistingCustomer).ToList();
        var reason = $"Import #{batch.Id} ({batch.FileName})";
        var now = clock.UtcNow;
        var assignmentsChanged = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. Groups drafted in this import become real delivery groups.
            var groupForDraft = new Dictionary<int, DeliveryGroup>();
            foreach (var draft in drafts)
            {
                var group = new DeliveryGroup { Name = draft.Name, Description = draft.Description, Status = RecordStatus.Active };
                db.DeliveryGroups.Add(group);
                groupForDraft[draft.Id] = group;
            }
            await db.SaveChangesAsync(ct);
            foreach (var draft in drafts)
            {
                draft.CreatedDeliveryGroupId = groupForDraft[draft.Id].Id;
                audit.Record(AuditActions.GroupCreated, nameof(DeliveryGroup), draft.Name, $"Delivery group created: {draft.Name}", new { ImportBatchId = batch.Id });
            }

            int? TargetGroupId(ImportRow r) =>
                r.TargetDeliveryGroupId ?? (r.TargetDraftGroupId is { } d ? groupForDraft[d].Id : null);

            // 2. New customers, in chunks to keep memory and statement sizes flat for large files.
            var created = new List<(ImportRow Row, Customer Customer)>(newRows.Count);
            foreach (var chunk in newRows.Chunk(ChunkSize))
            {
                foreach (var row in chunk)
                {
                    var customer = new Customer
                    {
                        CustomerId = row.CustomerId!,
                        CustomerName = row.CustomerName!,
                        Pincode = row.Pincode,
                        Locality = row.Locality,
                        City = row.City,
                        OriginalPlan = row.RawPlan?.Trim(),
                        PlanId = row.PlanId,
                        PricingTierId = row.PricingTierId,
                        SourceImportBatchId = batch.Id,
                        Status = CustomerStatus.Active
                    };
                    customer.SetPhoneNumber(row.RawPhoneNumber!);
                    customer.SetLocation(row.RawLocation, row.NormalizedLocation);
                    db.Customers.Add(customer);
                    created.Add((row, customer));
                }
                await db.SaveChangesAsync(ct);
            }

            // 3. Assignments + history for new customers.
            foreach (var (row, customer) in created)
            {
                var groupId = TargetGroupId(row);
                if (groupId is { } g)
                {
                    AddAssignment(customer.Id, null, g, now, reason);
                    assignmentsChanged++;
                }
                audit.Record(AuditActions.CustomerCreated, nameof(Customer), customer.CustomerId,
                    $"Customer created by import: {customer.CustomerId}",
                    new { ImportBatchId = batch.Id, row.RowNumber, customer.PlanId, DeliveryGroupId = groupId });
            }

            // 4. Existing customers: details untouched; group changes go through the one-active-group rule with history.
            var existingIds = existingRows.Select(r => r.ExistingCustomerDbId!.Value).ToList();
            var active = new Dictionary<int, CustomerDeliveryGroupAssignment>();
            foreach (var chunk in existingIds.Chunk(ChunkSize))
            {
                var ids = chunk.ToList();
                foreach (var a in await db.CustomerDeliveryGroupAssignments.Where(a => a.IsActive && ids.Contains(a.CustomerId)).ToListAsync(ct))
                    active[a.CustomerId] = a;
            }
            foreach (var row in existingRows)
            {
                if (TargetGroupId(row) is not { } target) continue;
                var customerDbId = row.ExistingCustomerDbId!.Value;
                active.TryGetValue(customerDbId, out var current);
                if (current?.DeliveryGroupId == target) continue;

                if (current is not null) current.IsActive = false;
                AddAssignment(customerDbId, current?.DeliveryGroupId, target, now, reason);
                assignmentsChanged++;
                audit.Record(current is null ? AuditActions.CustomerAssigned : AuditActions.CustomerReassigned, nameof(Customer), row.CustomerId,
                    current is null ? $"{row.CustomerId} assigned to a delivery group by import" : $"{row.CustomerId} moved to another delivery group by import",
                    new { ImportBatchId = batch.Id, PreviousGroupId = current?.DeliveryGroupId, NewGroupId = target });
            }
            await db.SaveChangesAsync(ct);

            // 5. Manager-confirmed plan mappings are remembered for future files.
            await SaveAliasesAsync(manualMappings, now, ct);

            // 6. Batch result.
            batch.Status = ImportBatchStatus.Completed;
            batch.TotalRows = totalRows;
            batch.ImportedCount = rows.Count;
            batch.CustomersCreated = created.Count;
            batch.ExistingCustomersMatched = existingRows.Count;
            batch.GroupsCreated = drafts.Count;
            batch.AssignmentsChanged = assignmentsChanged;
            batch.FailureReason = null;
            batch.CompletedAt = clock.UtcNow;
            batch.SaveDurationMs = stopwatch.ElapsedMilliseconds;
            audit.Record(AuditActions.ImportPerformed, nameof(ImportBatch), batch.Id.ToString(),
                $"Import saved: {batch.FileName} ({created.Count} new customers, {assignmentsChanged} assignments)",
                new { batch.ImportedCount, batch.CustomersCreated, batch.ExistingCustomersMatched, batch.GroupsCreated, batch.AssignmentsChanged, SkippedRows = totalRows - rows.Count });
            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Import {BatchId} save failed and was rolled back", batchId);
            await RecordFailureAsync(batchId, ex);
            throw new BusinessRuleException($"Saving failed and every change was rolled back. Nothing was saved. ({ex.GetBaseException().Message})");
        }

        return new ImportCommitResultDto(batch.Id, batch.CustomersCreated, batch.ExistingCustomersMatched, batch.GroupsCreated,
            batch.AssignmentsChanged, batch.ImportedCount, totalRows - rows.Count, batch.SaveDurationMs);
    }

    private void AddAssignment(int customerDbId, int? previousGroupId, int groupId, DateTime now, string reason)
    {
        db.CustomerDeliveryGroupAssignments.Add(new CustomerDeliveryGroupAssignment
        {
            CustomerId = customerDbId, DeliveryGroupId = groupId, AssignedAt = now, AssignedBy = currentUser.UserName, IsActive = true
        });
        db.CustomerDeliveryGroupAssignmentHistory.Add(new CustomerDeliveryGroupAssignmentHistory
        {
            CustomerId = customerDbId, PreviousGroupId = previousGroupId, NewGroupId = groupId,
            ChangedAt = now, ChangedBy = currentUser.UserName, Reason = reason
        });
    }

    private async Task SaveAliasesAsync(IReadOnlyList<ImportPlanMapping> mappings, DateTime now, CancellationToken ct)
    {
        if (mappings.Count == 0) return;
        var keys = mappings.Select(m => m.PlanKey).ToList();
        var existing = await db.PlanAliases.Where(a => keys.Contains(a.AliasKey)).ToDictionaryAsync(a => a.AliasKey, ct);
        foreach (var m in mappings)
        {
            if (existing.TryGetValue(m.PlanKey, out var alias))
            {
                alias.PlanId = m.PlanId!.Value;
                alias.PricingTierId = m.PricingTierId;
            }
            else
            {
                db.PlanAliases.Add(new PlanAlias
                {
                    AliasKey = m.PlanKey, OriginalText = m.ExampleText, PlanId = m.PlanId!.Value, PricingTierId = m.PricingTierId,
                    CreatedAt = now, CreatedBy = currentUser.UserName
                });
            }
        }
    }

    /// <summary>Re-checks what may have changed since validation. Runs before any write.</summary>
    private async Task EnsureStillValidAsync(IReadOnlyList<ImportRow> rows, IReadOnlyList<ImportDraftGroup> drafts, CancellationToken ct)
    {
        var problems = new List<string>();

        var newIds = rows.Where(r => !r.IsExistingCustomer).Select(r => r.CustomerId!.ToLower()).ToList();
        var taken = new List<string>();
        foreach (var chunk in newIds.Chunk(ChunkSize))
        {
            var ids = chunk.ToList();
            taken.AddRange(await db.Customers.Where(c => ids.Contains(c.CustomerId.ToLower())).Select(c => c.CustomerId).ToListAsync(ct));
        }
        if (taken.Count > 0)
            problems.Add($"{taken.Count} CustomerID(s) were added by someone else since this file was validated ({string.Join(", ", taken.Take(5))}). Upload the file again.");

        var existingIds = rows.Where(r => r.IsExistingCustomer).Select(r => r.ExistingCustomerDbId!.Value).Distinct().ToList();
        var stillThere = 0;
        foreach (var chunk in existingIds.Chunk(ChunkSize))
        {
            var ids = chunk.ToList();
            stillThere += await db.Customers.CountAsync(c => ids.Contains(c.Id), ct);
        }
        if (stillThere != existingIds.Count) problems.Add("Some existing customers in this file no longer exist. Upload the file again.");

        var groupIds = rows.Where(r => r.TargetDeliveryGroupId != null).Select(r => r.TargetDeliveryGroupId!.Value).Distinct().ToList();
        var activeGroups = await db.DeliveryGroups.CountAsync(g => groupIds.Contains(g.Id) && g.Status == RecordStatus.Active, ct);
        if (activeGroups != groupIds.Count) problems.Add("A delivery group used in this import has been deactivated. Reassign those customers.");

        var draftNames = drafts.Select(d => d.Name.ToLower()).ToList();
        var clashes = await db.DeliveryGroups.Where(g => draftNames.Contains(g.Name.ToLower())).Select(g => g.Name).ToListAsync(ct);
        if (clashes.Count > 0) problems.Add($"Group name(s) now already in use: {string.Join(", ", clashes)}. Rename the new group(s).");

        if (problems.Count > 0) throw new BusinessRuleException(string.Join(" ", problems));
    }

    private async Task RecordFailureAsync(int batchId, Exception ex)
    {
        try
        {
            var batch = await db.ImportBatches.FirstAsync(b => b.Id == batchId);
            batch.FailureReason = $"Last save attempt failed and was rolled back at {clock.UtcNow:u}: {ex.GetBaseException().Message}";
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception recordEx)
        {
            logger.LogWarning(recordEx, "Could not record the failure reason for import {BatchId}", batchId);
        }
    }
}
