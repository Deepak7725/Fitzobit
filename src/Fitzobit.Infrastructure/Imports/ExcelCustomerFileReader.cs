using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Fitzobit.Application.Imports;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Imports;

/// <summary>
/// Reads customer rows from an uploaded .xlsx. The file is untrusted:
/// the package is inspected before parsing (macros and oversized content are rejected), formulas are never
/// evaluated (their saved value is used and flagged), external links are ignored, and every value is read as text so
/// phone numbers and IDs keep their digits.
/// </summary>
public sealed class ExcelCustomerFileReader(IOptions<ImportOptions> options) : ICustomerFileReader
{
    private const int HeaderSearchRows = 20;
    private const int MaxZipEntries = 5000;
    private const string PreferredSheet = "Customers";

    public CustomerFileReadResult Read(Stream content, string fileName)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        if (buffer.Length > options.Value.MaxFileSizeBytes)
            return CustomerFileReadResult.Fail($"The file is larger than {options.Value.MaxFileSizeBytes / 1024 / 1024} MB.");

        var notices = new List<string>();
        var packageError = InspectPackage(buffer, notices);
        if (packageError is not null) return CustomerFileReadResult.Fail(packageError);

        buffer.Position = 0;
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(buffer, new LoadOptions { RecalculateAllFormulas = false });
        }
        catch (Exception)
        {
            return CustomerFileReadResult.Fail("The workbook could not be opened. It may be damaged or password-protected. Save it again as .xlsx and retry.");
        }

        using (workbook)
            return ReadWorkbook(workbook, notices);
    }

    /// <summary>Checks the .xlsx (zip) container before any parser touches it.</summary>
    private string? InspectPackage(MemoryStream buffer, List<string> notices)
    {
        try
        {
            buffer.Position = 0;
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxZipEntries) return "The workbook contains too many parts to be processed safely.";

            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/').ToLowerInvariant()).ToList();
            if (!names.Contains("[content_types].xml") || !names.Contains("xl/workbook.xml"))
                return "The file is not a valid Excel workbook (.xlsx).";
            if (names.Any(n => n.EndsWith("vbaproject.bin", StringComparison.Ordinal)))
                return "The workbook contains macros. Macro content is not accepted; save it as a plain Excel Workbook (.xlsx).";

            var uncompressed = zip.Entries.Sum(e => e.Length);
            if (uncompressed > options.Value.MaxUncompressedBytes)
                return "The workbook expands to more data than can be processed safely.";

            if (names.Any(n => n.StartsWith("xl/externallinks/", StringComparison.Ordinal)))
                notices.Add("The workbook has links to other files; they were ignored.");
            return null;
        }
        catch (InvalidDataException)
        {
            return "The file is not a valid Excel workbook (.xlsx). It could not be read.";
        }
    }

    private CustomerFileReadResult ReadWorkbook(XLWorkbook workbook, List<string> notices)
    {
        var sheets = workbook.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible)
            .OrderBy(w => string.Equals(w.Name, PreferredSheet, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(w => w.Position)
            .ToList();
        if (sheets.Count == 0) return CustomerFileReadResult.Fail("The workbook has no visible worksheets.");

        foreach (var sheet in sheets)
        {
            var header = FindHeaderRow(sheet);
            if (header is null) continue;
            return ReadSheet(sheet, header, notices);
        }

        var firstRow = sheets[0].FirstRowUsed();
        var seen = firstRow is null ? "(the sheet is empty)" : string.Join(", ", firstRow.CellsUsed().Select(c => $"\"{c.GetString().Trim()}\"").Take(10));
        return CustomerFileReadResult.Fail(
            $"No worksheet has the required columns ({string.Join(", ", CustomerFileHeaders.Required)}). " +
            $"First row of \"{sheets[0].Name}\": {seen}.");
    }

    /// <summary>The first row (within the top rows) that names at least three of the required columns.</summary>
    private static IXLRow? FindHeaderRow(IXLWorksheet sheet)
    {
        var last = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, HeaderSearchRows);
        for (var r = 1; r <= last; r++)
        {
            var row = sheet.Row(r);
            var found = row.CellsUsed().Select(c => CustomerFileHeaders.Canonical(c.GetString())).Where(c => c != null).Distinct().Count();
            if (found >= 3) return row;
        }
        return null;
    }

    private CustomerFileReadResult ReadSheet(IXLWorksheet sheet, IXLRow headerRow, List<string> notices)
    {
        var errors = new List<string>();
        var columns = new Dictionary<string, int>();
        var ignored = new List<string>();

        foreach (var cell in headerRow.CellsUsed())
        {
            var text = cell.GetString().Trim();
            var canonical = CustomerFileHeaders.Canonical(text);
            var letter = cell.Address.ColumnLetter;
            if (canonical is null)
            {
                if (text.Length > 0) ignored.Add($"\"{text}\" ({letter})");
            }
            else if (columns.TryGetValue(canonical, out var existing))
            {
                errors.Add($"Duplicate column {canonical}: column {XLHelper.GetColumnLetterFromNumber(existing)} and column {letter} both map to it. Keep only one.");
            }
            else columns[canonical] = cell.Address.ColumnNumber;
        }

        var missing = CustomerFileHeaders.Required.Where(r => !columns.ContainsKey(r)).ToList();
        if (missing.Count > 0) errors.Add($"Missing required column(s): {string.Join(", ", missing)}.");
        if (errors.Count > 0) return new CustomerFileReadResult(false, sheet.Name, errors, notices, [], 0);

        if (ignored.Count > 0) notices.Add($"Ignored column(s) not used by Fitzobit: {string.Join(", ", ignored)}.");

        var rows = new List<RawCustomerRow>();
        var emptyRows = 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (var r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var notes = new List<string>();
            string? Value(string column) => CellText(sheet.Cell(r, columns[column]), notes);

            var row = new RawCustomerRow(r,
                Value(CustomerFileHeaders.CustomerId),
                Value(CustomerFileHeaders.CustomerName),
                Value(CustomerFileHeaders.PhoneNumber),
                Value(CustomerFileHeaders.Location),
                Value(CustomerFileHeaders.Plan),
                notes);

            if (new[] { row.CustomerId, row.CustomerName, row.PhoneNumber, row.Location, row.Plan }.All(string.IsNullOrWhiteSpace))
            {
                emptyRows++;
                continue;
            }

            rows.Add(row);
            if (rows.Count > options.Value.MaxRows) break; // the caller reports the limit; stop reading early
        }

        if (emptyRows > 0) notices.Add($"{emptyRows} empty row(s) were skipped.");
        if (headerRow.RowNumber() > 1) notices.Add($"Headers were found on row {headerRow.RowNumber()} of \"{sheet.Name}\".");
        return new CustomerFileReadResult(true, sheet.Name, [], notices, rows, emptyRows);
    }

    /// <summary>Cell content as text. Formulas are not evaluated: the value Excel saved is used and the row is flagged.</summary>
    private static string? CellText(IXLCell cell, List<string> notes)
    {
        XLCellValue value;
        if (cell.HasFormula)
        {
            value = cell.CachedValue;
            notes.Add($"Cell {cell.Address.ToStringRelative()} contains a formula; its saved value was used");
        }
        else value = cell.Value;

        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Text => value.GetText(),
            XLDataType.Number => FormatNumber(value.GetNumber()),
            XLDataType.Boolean => value.GetBoolean() ? "TRUE" : "FALSE",
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString(),
            XLDataType.Error => null,
            _ => cell.GetString()
        };
    }

    /// <summary>Whole numbers (phone numbers, IDs) are written out in full, never in scientific notation.</summary>
    private static string FormatNumber(double number) =>
        Math.Abs(number) < 1e15 && Math.Abs(number % 1) < double.Epsilon
            ? ((decimal)number).ToString("0", CultureInfo.InvariantCulture)
            : number.ToString("0.############", CultureInfo.InvariantCulture);
}
