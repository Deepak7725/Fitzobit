using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Imports;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Administration;

public sealed record PlanAliasDto(int Id, string OriginalText, string AliasKey, int PlanId, string PlanName, string? TierName, DateTime CreatedAt, string? CreatedBy);

/// <summary>Plan administration (Admin): activate/deactivate plans and manage remembered plan-name mappings.</summary>
public sealed class PlanAdminService(IFitzobitDbContext db, IAuditLogger audit)
{
    public async Task SetStatusAsync(int planId, bool active, CancellationToken ct = default)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException($"Plan {planId} was not found.");
        var status = active ? RecordStatus.Active : RecordStatus.Inactive;
        if (plan.Status == status) return;
        var old = plan.Status;
        plan.Status = status;
        audit.RecordChange(AuditActions.PlanUpdated, nameof(Plan), plan.Code, $"Plan {(active ? "activated" : "deactivated")}: {plan.Name}",
            new Dictionary<string, object?> { ["Status"] = old.ToString() }, new Dictionary<string, object?> { ["Status"] = status.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlanAliasDto>> ListAliasesAsync(CancellationToken ct = default) =>
        await db.PlanAliases.AsNoTracking().OrderBy(a => a.OriginalText)
            .Select(a => new PlanAliasDto(a.Id, a.OriginalText, a.AliasKey, a.PlanId, a.Plan!.Name,
                a.PricingTier != null ? a.PricingTier.TierName : null, a.CreatedAt, a.CreatedBy))
            .ToListAsync(ct);

    public async Task DeleteAliasAsync(int id, CancellationToken ct = default)
    {
        var alias = await db.PlanAliases.Include(a => a.Plan).FirstOrDefaultAsync(a => a.Id == id, ct)
                    ?? throw new NotFoundException($"Plan mapping {id} was not found.");
        db.PlanAliases.Remove(alias);
        audit.RecordChange(AuditActions.PlanAliasDeleted, nameof(PlanAlias), alias.AliasKey, $"Remembered plan mapping removed: \"{alias.OriginalText}\"",
            new Dictionary<string, object?> { ["OriginalText"] = alias.OriginalText, ["Plan"] = alias.Plan?.Name }, new Dictionary<string, object?>());
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Read access to the audit trail (Admin).</summary>
public sealed class AuditQueryService(IFitzobitDbContext db)
{
    public async Task<PagedResult<AuditEntryDto>> ListAsync(string? entityType, string? action, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(a => a.Summary.ToLower().Contains(s) || (a.EntityKey != null && a.EntityKey.ToLower().Contains(s))
                             || (a.PerformedBy != null && a.PerformedBy.ToLower().Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditEntryDto>(items.Select(CustomerQueryService.ToDto).ToList(), total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
}
