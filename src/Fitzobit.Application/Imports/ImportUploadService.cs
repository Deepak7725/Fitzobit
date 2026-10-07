using System.Diagnostics;
using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Imports.Plans;
using Fitzobit.Application.Imports.Validation;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.Imports;

public sealed record ImportUploadResult(int BatchId, string Status, IReadOnlyList<string> Errors);

/// <summary>
/// Step 1–2: reads an uploaded workbook, validates every row and stages the result as a draft import batch.
/// Nothing is written to the customer tables here; that only happens on Confirm &amp; Save.
/// </summary>
public sealed class ImportUploadService(
    IFitzobitDbContext db,
    ICustomerFileReader reader,
    CustomerRowValidator validator,
    PlanCatalogue planCatalogue,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<ImportOptions> options)
{
    private const string AuditImportUploaded = "ImportUploaded";

    public async Task<ImportUploadResult> UploadAsync(Stream content, string fileName, long length, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var safeName = Path.GetFileName(fileName);
        var batch = new ImportBatch
        {
            FileName = safeName.Length > 255 ? safeName[..255] : safeName,
            FileSizeBytes = length,
            UploadedAt = clock.UtcNow,
            UploadedBy = currentUser.UserName,
            Status = ImportBatchStatus.Pending
        };

        var errors = CheckFile(safeName, length);
        var read = errors.Count == 0 ? reader.Read(content, safeName) : CustomerFileReadResult.Fail([.. errors]);
        if (read.Success && read.Rows.Count == 0)
            read = read with { Success = false, Errors = ["The worksheet has headers but no customer rows"] };
        if (read.Success && read.Rows.Count > options.Value.MaxRows)
            read = read with { Success = false, Errors = [$"The file has {read.Rows.Count:N0} rows; the limit is {options.Value.MaxRows:N0}"] };

        batch.SheetName = read.SheetName;
        batch.EmptyRowsSkipped = read.EmptyRowsSkipped;
        batch.FileMessages = read.Notices.Count == 0 ? null : string.Join('\n', read.Notices);

        if (!read.Success)
        {
            batch.Status = ImportBatchStatus.Failed;
            batch.FailureReason = string.Join('\n', read.Errors);
            batch.ValidationDurationMs = stopwatch.ElapsedMilliseconds;
            db.ImportBatches.Add(batch);
            audit.Record(AuditImportUploaded, nameof(ImportBatch), batch.FileName, $"Upload rejected: {batch.FileName}", new { read.Errors });
            await db.SaveChangesAsync(ct);
            return new ImportUploadResult(batch.Id, batch.Status.ToString(), read.Errors);
        }

        var matcher = await planCatalogue.CreateMatcherAsync(ct);
        var planNames = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var mappings = BuildPlanMappings(read.Rows, matcher);
        var context = await BuildContextAsync(read.Rows, mappings, planNames, ct);

        var rows = read.Rows.Select(raw => validator.Validate(raw, context)).ToList();
        foreach (var mapping in mappings.Values)
            batch.PlanMappings.Add(mapping);

        batch.TotalRows = rows.Count;
        batch.ValidRows = rows.Count(r => r.Status == ImportRowStatus.Valid);
        batch.WarningRows = rows.Count(r => r.Status == ImportRowStatus.Warning);
        batch.InvalidRows = rows.Count(r => r.Status == ImportRowStatus.Error);
        batch.Status = ImportBatchStatus.Validated;
        batch.ValidationDurationMs = stopwatch.ElapsedMilliseconds;

        // The batch is inserted first so its rows are written with a known key: propagating a new key to thousands of
        // rows in one graph is very slow in EF Core. One transaction keeps the upload all-or-nothing.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.ImportBatches.Add(batch);
        audit.Record(AuditImportUploaded, nameof(ImportBatch), batch.FileName, $"File uploaded and validated: {batch.FileName}",
            new { batch.TotalRows, batch.ValidRows, batch.WarningRows, batch.InvalidRows });
        await db.SaveChangesAsync(ct);
        foreach (var row in rows) row.ImportBatchId = batch.Id;
        db.ImportRows.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ImportUploadResult(batch.Id, batch.Status.ToString(), []);
    }

    private List<string> CheckFile(string fileName, long length)
    {
        var errors = new List<string>();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".xlsx")
            errors.Add(extension is ".xls" or ".xlsm" or ".xlsb"
                ? $"\"{extension}\" files are not accepted. Save the workbook as Excel Workbook (.xlsx) and upload again"
                : "Only Excel workbooks (.xlsx) can be uploaded");
        if (length <= 0) errors.Add("The file is empty");
        else if (length > options.Value.MaxFileSizeBytes)
            errors.Add($"The file is {length / 1024.0 / 1024.0:N1} MB; the limit is {options.Value.MaxFileSizeBytes / 1024 / 1024} MB");
        return errors;
    }

    private static Dictionary<string, ImportPlanMapping> BuildPlanMappings(IReadOnlyList<RawCustomerRow> rows, PlanMatcher matcher)
    {
        var result = new Dictionary<string, ImportPlanMapping>(StringComparer.Ordinal);
        foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.Plan)).GroupBy(r => PlanTextNormalizer.Key(r.Plan)))
        {
            if (group.Key.Length == 0) continue;
            var example = group.First().Plan!.Trim();
            var match = matcher.Match(example);
            result[group.Key] = new ImportPlanMapping
            {
                PlanKey = group.Key,
                ExampleText = example.Length > 300 ? example[..300] : example,
                RowCount = group.Count(),
                PlanId = match.PlanId,
                PricingTierId = match.PricingTierId,
                Method = match.Method,
                Status = match.Status,
                CandidatePlanIdsJson = match.CandidatePlanIds.Count == 0 ? null : JsonSerializer.Serialize(match.CandidatePlanIds),
                Note = match.Note
            };
        }
        return result;
    }

    private async Task<RowValidationContext> BuildContextAsync(
        IReadOnlyList<RawCustomerRow> rows,
        IReadOnlyDictionary<string, ImportPlanMapping> mappings,
        IReadOnlyDictionary<int, string> planNames,
        CancellationToken ct)
    {
        var activeGroups = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new { a.CustomerId, a.DeliveryGroupId })
            .ToDictionaryAsync(a => a.CustomerId, a => a.DeliveryGroupId, ct);

        var existing = (await db.Customers.AsNoTracking()
                .Select(c => new { c.Id, c.CustomerId, c.CustomerName, c.NormalizedPhoneNumber, c.NormalizedLocation, c.PlanId })
                .ToListAsync(ct))
            .Select(c => new ExistingCustomerInfo(c.Id, c.CustomerId, c.CustomerName, c.NormalizedPhoneNumber, c.NormalizedLocation,
                c.PlanId, activeGroups.TryGetValue(c.Id, out var g) ? g : null))
            .ToList();

        var existingById = new Dictionary<string, ExistingCustomerInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existing) existingById.TryAdd(c.CustomerId, c);

        var rowsById = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var id = r.CustomerId?.Trim();
            if (string.IsNullOrEmpty(id)) continue;
            if (!rowsById.TryGetValue(id, out var list)) rowsById[id] = list = [];
            list.Add(r.RowNumber);
        }

        var rowsByPhone = rows
            .Select(r => (r.RowNumber, CustomerId: r.CustomerId?.Trim(), Phone: PhoneNumberValidator.Validate(r.PhoneNumber).Normalized))
            .Where(r => r.Phone is { Length: 10 })
            .ToLookup(r => r.Phone!, r => (r.RowNumber, r.CustomerId));

        return new RowValidationContext
        {
            ExistingById = existingById,
            ExistingByPhone = existing.Where(c => c.NormalizedPhoneNumber != null).ToLookup(c => c.NormalizedPhoneNumber!),
            RowsByCustomerId = rowsById,
            RowsByPhone = rowsByPhone,
            PlanMappings = mappings,
            PlanName = id => planNames.GetValueOrDefault(id, $"Plan #{id}")
        };
    }
}
