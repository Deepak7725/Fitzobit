using ClosedXML.Excel;
using Fitzobit.Application.Catalogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Catalogue;

public sealed class CatalogueSeedOptions
{
    public const string SectionName = "CatalogueSeed";

    public bool Enabled { get; set; } = true;

    /// <summary>Path to the WhatsApp catalogue export. Relative paths are resolved against the configured base path.</summary>
    public string FilePath { get; set; } = "Fitzobit_WhatsApp_Catalogue.xlsx";

    /// <summary>
    /// Display names for the workbook's item groupings (workbook name -> category shown in the app).
    /// Groupings not listed keep the workbook's own name.
    /// </summary>
    public Dictionary<string, string> CategoryNames { get; set; } = new();

    /// <summary>Set by the host: directory that relative <see cref="FilePath"/> values are resolved against.</summary>
    public string? BasePath { get; set; }

    public string ResolveFilePath() =>
        Path.IsPathRooted(FilePath) || BasePath is null ? FilePath : Path.GetFullPath(Path.Combine(BasePath, FilePath));
}

/// <summary>
/// Reads Fitzobit_WhatsApp_Catalogue.xlsx (sheets: Catalogue, Plans, Pricing Tiers, Categories, Raw Data).
/// Columns are located by header text, and values are copied verbatim. "Not available / Not visible"
/// placeholders become nulls.
/// </summary>
public sealed class ExcelCatalogueSource(IOptions<CatalogueSeedOptions> options, ILogger<ExcelCatalogueSource> logger) : ICatalogueSource
{
    private const string NotAvailable = "Not available / Not visible";

    public Task<CatalogueSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = options.Value.ResolveFilePath();
        if (!File.Exists(path))
        {
            logger.LogWarning("Catalogue workbook not found at {Path}", path);
            return Task.FromResult<CatalogueSnapshot?>(null);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        return Task.FromResult<CatalogueSnapshot?>(Read(workbook, Path.GetFileName(path)));
    }

    private CatalogueSnapshot Read(XLWorkbook workbook, string fileName)
    {
        var catalogue = Rows(workbook, "Catalogue");
        var plans = Rows(workbook, "Plans").ToDictionary(r => Int(r, "Display Order"));
        var tiers = Rows(workbook, "Pricing Tiers").ToLookup(r => Int(r, "Display Order"));
        var raw = Rows(workbook, "Raw Data");
        var categoryByItem = ReadCategories(workbook);

        var channel = raw.Select(r => Text(r, "Source")).FirstOrDefault(s => s != null);
        var source = channel is null ? fileName : $"{channel} | {fileName}";

        var items = catalogue.Select(row =>
        {
            var order = Int(row, "Display Order");
            var name = Text(row, "Product/Plan Name") ?? throw new InvalidDataException($"Catalogue row {order} has no name.");
            plans.TryGetValue(order, out var plan);

            return new CatalogueSnapshotItem(
                DisplayOrder: order,
                Name: name,
                Category: Text(row, "Category") ?? categoryByItem.GetValueOrDefault(name),
                Term: plan is null ? null : Text(plan, "Fitzobit Term"),
                Description: Text(row, "Description"),
                DisplayPrice: Decimal(row, "Price"),
                Currency: Text(row, "Currency") ?? "INR",
                DurationText: plan is null ? null : Text(plan, "Billing/Duration"),
                IncludedItems: plan is null ? null : Text(plan, "Included Services/Items"),
                Availability: Text(row, "Availability"),
                SourceNotes: plan is null ? null : Text(plan, "Notes"),
                Tiers: tiers[order].Select(t => new CatalogueSnapshotTier(
                    TierName: Text(t, "Tier (as written)") ?? throw new InvalidDataException($"Tier for item {order} has no name."),
                    Price: Decimal(t, "Tier Price"),
                    Currency: Text(t, "Currency") ?? "INR",
                    PeriodText: Text(t, "Period"),
                    MealsText: Text(t, "Meals"),
                    Description: Text(t, "Per-day / Extra (as written)"),
                    Source: Text(t, "Source"))).ToList());
        }).ToList();

        logger.LogInformation("Read {Items} catalogue items and {Tiers} pricing tiers from {File}",
            items.Count, items.Sum(i => i.Tiers.Count), fileName);
        return new CatalogueSnapshot(source, items);
    }

    /// <summary>Item name -> category, from the workbook's Categories sheet (rows with at least one item).</summary>
    private Dictionary<string, string> ReadCategories(XLWorkbook workbook)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!workbook.TryGetWorksheet("Categories", out _)) return result;

        foreach (var row in Rows(workbook, "Categories"))
        {
            var group = Text(row, "Category");
            var members = Text(row, "Items");
            if (group is null || members is null || Int(row, "Number of Items") == 0) continue;

            var display = options.Value.CategoryNames.GetValueOrDefault(group, group);
            foreach (var member in members.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result[member] = display;
        }
        return result;
    }

    private static List<Dictionary<string, IXLCell>> Rows(XLWorkbook workbook, string sheetName)
    {
        if (!workbook.TryGetWorksheet(sheetName, out var sheet))
            throw new InvalidDataException($"Catalogue workbook has no \"{sheetName}\" sheet.");

        var headerRow = sheet.FirstRowUsed() ?? throw new InvalidDataException($"Sheet \"{sheetName}\" is empty.");
        var headers = headerRow.CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber);

        return sheet.RowsUsed()
            .Where(r => r.RowNumber() > headerRow.RowNumber())
            .Select(r => headers.ToDictionary(h => h.Key, h => r.Cell(h.Value)))
            .ToList();
    }

    private static string? Text(Dictionary<string, IXLCell> row, string column)
    {
        if (!row.TryGetValue(column, out var cell)) return null;
        var value = cell.GetString().Replace("\r\n", "\n");
        return string.IsNullOrWhiteSpace(value) || value.Trim() == NotAvailable ? null : value;
    }

    private static decimal Decimal(Dictionary<string, IXLCell> row, string column) =>
        row.TryGetValue(column, out var cell) && cell.TryGetValue<decimal>(out var value)
            ? value
            : throw new InvalidDataException($"Column \"{column}\" does not contain a number.");

    private static int Int(Dictionary<string, IXLCell> row, string column) =>
        row.TryGetValue(column, out var cell) && cell.TryGetValue<int>(out var value) ? value : 0;
}
