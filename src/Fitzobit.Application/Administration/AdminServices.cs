using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Administration;

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
