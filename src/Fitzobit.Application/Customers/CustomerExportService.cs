using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.Customers;

public sealed class ExportOptions
{
    public const string SectionName = "Export";

    public int MaxRows { get; set; } = 100_000;

    /// <summary>Time zone used for dates in exported files (IANA or Windows id).</summary>
    public string TimeZone { get; set; } = "Asia/Kolkata";
}

public sealed record CustomerExportRow(
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? Location,
    string? Pincode,
    string? Plan,
    string? DeliveryGroup,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Describes an export: shown on the workbook's information sheet.</summary>
public sealed record CustomerExportInfo(string Scope, IReadOnlyList<KeyValuePair<string, string>> Filters, string ExportedBy, DateTime ExportedAtUtc, TimeZoneInfo TimeZone);

public interface ICustomerExcelWriter
{
    void Write(Stream output, IReadOnlyList<CustomerExportRow> rows, CustomerExportInfo info);
}

public enum ExportScope
{
    All,
    Filtered,
    Selected
}

public sealed record ExportFile(byte[] Content, string FileName, int RowCount);

/// <summary>
/// Exports customers to Excel. "Filtered" applies exactly the same filter and sort as the View Data screen, so the file
/// contains only the matching records; "Selected" exports the chosen customer ids.
/// </summary>
public sealed class CustomerExportService(
    IFitzobitDbContext db,
    CustomerQueryService queries,
    ICustomerExcelWriter writer,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<ExportOptions> options)
{
    public async Task<ExportFile> ExportAsync(ExportScope scope, CustomerFilter filter, IReadOnlyCollection<int>? selectedIds, CancellationToken ct = default)
    {
        var rows = queries.Rows();
        switch (scope)
        {
            case ExportScope.Filtered:
                rows = CustomerQueryService.ApplyFilter(rows, filter);
                break;
            case ExportScope.Selected:
                if (selectedIds is not { Count: > 0 }) throw new BusinessRuleException("Select at least one customer to export.");
                var ids = selectedIds.Distinct().ToList();
                rows = rows.Where(r => ids.Contains(r.Id));
                break;
        }

        var count = await rows.CountAsync(ct);
        if (count > options.Value.MaxRows)
            throw new BusinessRuleException($"{count:N0} customers match; the export limit is {options.Value.MaxRows:N0}. Narrow the filters and try again.");

        var data = await CustomerQueryService.ApplySort(rows, filter.Sort, filter.Desc)
            .Select(r => new CustomerExportRow(r.CustomerId, r.CustomerName, r.PhoneNumber, r.Location, r.Pincode, r.PlanName, r.GroupName,
                r.Status.ToString(), r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var describedFilters = scope == ExportScope.Filtered ? await DescribeAsync(filter, ct) : [];
        if (scope == ExportScope.Selected) describedFilters = [new("Selected customers", data.Count.ToString("N0"))];

        var timeZone = ResolveTimeZone();
        using var stream = new MemoryStream();
        writer.Write(stream, data, new CustomerExportInfo(scope.ToString(), describedFilters, currentUser.DisplayName, now, timeZone));

        var fileName = $"Fitzobit_Customers_{scope}_{TimeZoneInfo.ConvertTimeFromUtc(now, timeZone):yyyyMMdd_HHmmss}.xlsx";
        audit.Record(AuditActions.ExportPerformed, "Customer", null, $"Exported {data.Count:N0} customer(s) ({scope.ToString().ToLowerInvariant()})",
            new { Scope = scope.ToString(), Rows = data.Count, FileName = fileName, Filters = describedFilters.ToDictionary(k => k.Key, v => v.Value) });
        await db.SaveChangesAsync(ct);

        return new ExportFile(stream.ToArray(), fileName, data.Count);
    }

    private async Task<List<KeyValuePair<string, string>>> DescribeAsync(CustomerFilter f, CancellationToken ct)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(f.Search)) list.Add(new("Search", f.Search.Trim()));
        if (f.Group == "none") list.Add(new("Delivery group", "No group"));
        else if (int.TryParse(f.Group, out var g))
            list.Add(new("Delivery group", await db.DeliveryGroups.Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? $"#{g}"));
        if (f.PlanId is { } p)
            list.Add(new("Plan", await db.Plans.Where(x => x.Id == p).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? $"#{p}"));
        if (!string.IsNullOrWhiteSpace(f.Pincode)) list.Add(new("Pincode", f.Pincode.Trim()));
        if (!string.IsNullOrWhiteSpace(f.Location)) list.Add(new("Location contains", f.Location.Trim()));
        if (!string.IsNullOrWhiteSpace(f.Status)) list.Add(new("Status", f.Status));
        if (f.ImportBatchId is { } b)
            list.Add(new("Import", await db.ImportBatches.Where(x => x.Id == b).Select(x => $"#{x.Id} {x.FileName}").FirstOrDefaultAsync(ct) ?? $"#{b}"));
        if (list.Count == 0) list.Add(new("Filters", "None (all customers)"));
        return list;
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
