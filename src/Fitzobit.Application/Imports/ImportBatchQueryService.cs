using Fitzobit.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Imports;

public sealed record ImportBatchDto(
    int Id,
    string FileName,
    DateTime UploadedAt,
    string? UploadedBy,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int WarningRows,
    int ImportedCount,
    int CustomersCreated,
    int GroupsCreated,
    int AssignmentsChanged,
    string Status,
    string? FailureReason,
    long ValidationDurationMs,
    long SaveDurationMs,
    DateTime? CompletedAt);

public sealed class ImportBatchQueryService(IFitzobitDbContext db)
{
    public async Task<IReadOnlyList<ImportBatchDto>> ListAsync(int? take = null, CancellationToken cancellationToken = default)
    {
        IQueryable<Domain.Imports.ImportBatch> query = db.ImportBatches.AsNoTracking().OrderByDescending(b => b.UploadedAt).ThenByDescending(b => b.Id);
        if (take is > 0) query = query.Take(take.Value);

        return await query
            .Select(b => new ImportBatchDto(b.Id, b.FileName, b.UploadedAt, b.UploadedBy, b.TotalRows, b.ValidRows,
                b.InvalidRows, b.WarningRows, b.ImportedCount, b.CustomersCreated, b.GroupsCreated, b.AssignmentsChanged,
                b.Status.ToString(), b.FailureReason, b.ValidationDurationMs, b.SaveDurationMs, b.CompletedAt))
            .ToListAsync(cancellationToken);
    }
}
