using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.Customers;

public sealed class ExportOptions
{
    public const string SectionName = "Export";

    public int MaxRows { get; set; } = 100_000;

    /// <summary>PDFs are meant for reading and printing; very long ones are slow to build on a small server.</summary>
    public int PdfMaxRows { get; set; } = 2_000;

    /// <summary>Time zone used for dates in exported files (IANA or Windows id).</summary>
    public string TimeZone { get; set; } = "Asia/Kolkata";
}

public sealed record CustomerExportRow(
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? AlternatePhoneNumber,
    string? Address,
    string? Pincode,
    string? GoogleMapUrl,
    TimeOnly? PreferredDeliveryTime,
    /// <summary>All packages as text, e.g. "Veg Medium ×1 · Monthly (12 Oct 2026 – 10 Nov 2026); Veg Oats ×2 · Weekly (…)".</summary>
    string? Packages,
    /// <summary>All notes, newest first, e.g. "19 Oct 2026, Siva Sankari: Call before delivery; 17 Oct 2026, …".</summary>
    string? Notes,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    int? GroupNumber,
    GeocodeStatus GeocodeStatus,
    GeoMatchLevel? GeocodeMatchLevel,
    decimal? Latitude,
    decimal? Longitude,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Describes an export (shown on the Excel information sheet and in the PDF heading).</summary>
/// <param name="Scope">Which rows, in words ("Filtered customers", "Selected customers", …).</param>
/// <param name="AllColumns">True for a full export; false when only the chosen (visible) columns are included.</param>
public sealed record CustomerExportInfo(
    string Scope,
    IReadOnlyList<KeyValuePair<string, string>> Filters,
    bool AllColumns,
    string ExportedBy,
    DateTime ExportedAtUtc,
    TimeZoneInfo TimeZone);

public enum ExportFormat
{
    Excel,
    Pdf
}

/// <summary>Writes one file format. Implementations live in Infrastructure (ClosedXML for Excel, MigraDoc for PDF).</summary>
public interface ICustomerExportWriter
{
    ExportFormat Format { get; }
    string ContentType { get; }
    string FileExtension { get; }

    /// <summary>Writes exactly <paramref name="columns"/> (in that order) for every row; nothing else is added to the table.</summary>
    void Write(Stream output, IReadOnlyList<CustomerExportColumn> columns, IReadOnlyList<CustomerExportRow> rows, CustomerExportInfo info);
}

public enum ExportScope
{
    /// <summary>Every customer, ignoring filters.</summary>
    All,
    /// <summary>Every customer matching the current filters (all pages).</summary>
    Filtered,
    /// <summary>The ticked customers.</summary>
    Selected,
    /// <summary>The page currently shown (same filters, sort, page and page size).</summary>
    Page
}

public sealed class CustomerExportRequest
{
    public ExportScope Scope { get; set; } = ExportScope.Filtered;
    public ExportFormat Format { get; set; } = ExportFormat.Excel;

    /// <summary>The Customers screen's filters and sort (used for "Filtered" and "Page", and for row order in every scope).</summary>
    public CustomerFilter? Filter { get; set; }

    /// <summary>Customer ids for "Selected".</summary>
    public List<int>? Ids { get; set; }

    /// <summary>Column keys in display order (normally the visible table columns). Empty = all columns.</summary>
    public List<string>? Columns { get; set; }

    /// <summary>For "Page": the page number and page size shown on screen.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed record ExportFile(byte[] Content, string FileName, string ContentType, int RowCount);

/// <summary>
/// Exports customers to Excel or PDF. Rows are selected on the server with exactly the same filter, sort and paging as the
/// Customers screen (never from what the browser happens to have loaded); the file contains only the requested columns.
/// </summary>
public sealed class CustomerExportService(
    IFitzobitDbContext db,
    CustomerQueryService queries,
    IEnumerable<ICustomerExportWriter> writers,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<ExportOptions> options)
{
    public async Task<ExportFile> ExportAsync(CustomerExportRequest request, CancellationToken ct = default)
    {
        var writer = writers.FirstOrDefault(w => w.Format == request.Format)
                     ?? throw new BusinessRuleException($"{request.Format} export is not available.");
        var columns = CustomerExportColumns.Resolve(request.Columns);
        var filter = request.Filter ?? new CustomerFilter();
        var limit = request.Format == ExportFormat.Pdf ? Math.Min(options.Value.PdfMaxRows, options.Value.MaxRows) : options.Value.MaxRows;

        var rows = queries.Rows();
        int? page = null, pageSize = null;
        switch (request.Scope)
        {
            case ExportScope.Filtered:
                rows = queries.ApplyFilter(rows, filter);
                break;
            case ExportScope.Page:
                page = Math.Max(1, request.Page ?? 1);
                pageSize = CustomerQueryService.PageSizes.Contains(request.PageSize ?? 0) ? request.PageSize!.Value : 25;
                rows = queries.ApplyFilter(rows, filter);
                break;
            case ExportScope.Selected:
                var ids = request.Ids?.Distinct().ToList();
                if (ids is not { Count: > 0 }) throw new BusinessRuleException("Select at least one customer to export.");
                if (ids.Count > limit) throw new BusinessRuleException($"{ids.Count:N0} customers are selected; the limit is {limit:N0}.");
                rows = rows.Where(r => ids.Contains(r.Id));
                break;
        }

        var sorted = CustomerQueryService.ApplySort(rows, filter.Sort, filter.Desc);
        if (page is { } p && pageSize is { } size)
        {
            sorted = sorted.Skip((p - 1) * size).Take(size);
        }
        else
        {
            var count = await rows.CountAsync(ct);
            if (count > limit)
                throw new BusinessRuleException(
                    $"{count:N0} customers match; the {(request.Format == ExportFormat.Pdf ? "PDF" : "Excel")} export limit is {limit:N0}. Narrow the filters and try again." +
                    (request.Format == ExportFormat.Pdf && count <= options.Value.MaxRows ? " Excel exports allow more rows." : ""));
        }

        var found = await sorted.ToListAsync(ct);
        var timeZone = ResolveTimeZone();
        var foundIds = found.Select(r => r.Id).ToList();
        var packages = await queries.PackageSummariesAsync(foundIds, ct);
        var notes = columns.Any(c => c.Key == "notes") ? await queries.NoteSummariesAsync(foundIds, ct) : [];
        var data = found.Select(r => new CustomerExportRow(r.CustomerId, r.CustomerName, r.PhoneNumber, r.AlternatePhoneNumber, r.Address, r.Pincode,
                r.GoogleMapUrl, r.PreferredDeliveryTime, packages.TryGetValue(r.Id, out var list) ? string.Join("; ", list.Select(p => p.Text)) : null,
                notes.TryGetValue(r.Id, out var n) ? string.Join("; ", n.Select(x => NoteText(x, timeZone))) : null, r.StartDate,
                r.EndDate, r.Status, r.GroupNumber, r.GeocodeStatus, r.GeocodeMatchLevel, r.Latitude, r.Longitude, r.CreatedAt, r.UpdatedAt))
            .ToList();

        var described = request.Scope switch
        {
            ExportScope.Filtered => await DescribeAsync(filter, ct),
            ExportScope.Page => [.. await DescribeAsync(filter, ct), new("Page", $"{page:N0} ({pageSize} rows per page)")],
            ExportScope.Selected => [new("Selected customers", data.Count.ToString("N0"))],
            _ => new List<KeyValuePair<string, string>> { new("Filters", "None (all customers)") }
        };
        var scopeLabel = request.Scope switch
        {
            ExportScope.Filtered => "Filtered customers",
            ExportScope.Page => "Current page",
            ExportScope.Selected => "Selected customers",
            _ => "All customers"
        };

        var now = clock.UtcNow;
        var allColumns = columns.Count == CustomerExportColumns.All.Count;
        using var stream = new MemoryStream();
        writer.Write(stream, columns, data, new CustomerExportInfo(scopeLabel, described, allColumns, currentUser.DisplayName, now, timeZone));

        var fileName = $"Fitzobit_Customers_{request.Scope}_{TimeZoneInfo.ConvertTimeFromUtc(now, timeZone):yyyyMMdd_HHmmss}.{writer.FileExtension}";
        audit.Record(AuditActions.ExportPerformed, "Customer", null,
            $"Exported {data.Count:N0} customer(s) to {request.Format} ({scopeLabel.ToLowerInvariant()}, {columns.Count} column(s))",
            new
            {
                Scope = request.Scope.ToString(),
                Format = request.Format.ToString(),
                Rows = data.Count,
                FileName = fileName,
                Columns = columns.Select(c => c.Header).ToList(),
                Filters = described.ToDictionary(k => k.Key, v => v.Value)
            });
        await db.SaveChangesAsync(ct);

        return new ExportFile(stream.ToArray(), fileName, writer.ContentType, data.Count);
    }

    private async Task<List<KeyValuePair<string, string>>> DescribeAsync(CustomerFilter f, CancellationToken ct)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(f.Search)) list.Add(new("Search", f.Search.Trim()));
        if (f.Group == "none") list.Add(new("Delivery group", "No group"));
        else if (int.TryParse(f.Group, out var g))
            list.Add(new("Delivery group", await db.DeliveryGroups.Where(x => x.Id == g).Select(x => "Group " + x.GroupNumber).FirstOrDefaultAsync(ct) ?? $"#{g}"));
        foreach (var (label, id) in new[] { ("Preference", f.PreferenceId), ("Meal type", f.MealTypeId), ("Frequency", f.FrequencyId), ("Status", f.StatusId) })
            if (id is { } entryId)
                list.Add(new(label, await db.DictionaryEntries.Where(x => x.Id == entryId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? $"#{entryId}"));
        if (!string.IsNullOrWhiteSpace(f.Pincode)) list.Add(new("Pincode", f.Pincode.Trim()));
        if (!string.IsNullOrWhiteSpace(f.Address)) list.Add(new("Address contains", f.Address.Trim()));
        if (f.Incomplete) list.Add(new("Details", "Incomplete only"));
        var geo = f.Geo?.ToLowerInvariant() switch
        {
            "located" => "Located",
            "approximate" => "Approximate (pincode)",
            "missing" => "Location unavailable",
            _ => null
        };
        if (geo is not null) list.Add(new("Location", geo));
        if (list.Count == 0) list.Add(new("Filters", "None (all customers)"));
        return list;
    }

    /// <summary>"19 Oct 2026, Siva Sankari: Call before delivery" (line breaks inside the note become spaces).</summary>
    private static string NoteText(NoteSummaryDto note, TimeZoneInfo timeZone)
    {
        var text = string.Join(' ', note.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        var date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(note.CreatedAt, DateTimeKind.Utc), timeZone);
        return note.CreatedBy is null ? $"{date:dd MMM yyyy}: {text}" : $"{date:dd MMM yyyy}, {note.CreatedBy}: {text}";
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
