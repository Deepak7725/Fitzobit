using ClosedXML.Excel;
using Fitzobit.Application.Customers;

namespace Fitzobit.Infrastructure.Exports;

/// <summary>
/// Writes the customer export: exactly the chosen columns, formatted header, autofilter, frozen header row, sized columns,
/// IDs/phones/pincodes as text (never numbers or scientific notation), real date cells, and a separate information sheet
/// describing the export (it never adds columns to the data sheet).
/// </summary>
public sealed class CustomerExcelWriter : ICustomerExportWriter
{
    private static readonly XLColor Navy = XLColor.FromHtml("#0E1D3D");

    public ExportFormat Format => ExportFormat.Excel;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => "xlsx";

    public void Write(Stream output, IReadOnlyList<CustomerExportColumn> columns, IReadOnlyList<CustomerExportRow> rows, CustomerExportInfo info)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Customers");
        var lastRow = Math.Max(2, rows.Count + 1);

        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var index = c + 1;
            sheet.Cell(1, index).Value = column.Header;
            sheet.Column(index).Width = Math.Max(column.ExcelWidth, column.Header.Length + 4);

            // Set the format before writing so values stay exactly as stored.
            var body = sheet.Range(2, index, lastRow, index).Style;
            switch (column.Kind)
            {
                case ExportColumnKind.Text or ExportColumnKind.Url: body.NumberFormat.Format = "@"; break;
                case ExportColumnKind.Integer: body.NumberFormat.Format = "0"; break;
                case ExportColumnKind.Coordinate: body.NumberFormat.Format = "0.000000"; break;
                case ExportColumnKind.Date: body.DateFormat.Format = "dd mmm yyyy"; break;
                case ExportColumnKind.DateTime: body.DateFormat.Format = "dd mmm yyyy hh:mm"; break;
                case ExportColumnKind.Time: body.DateFormat.Format = "hh:mm AM/PM"; break;
            }
            if (column.Kind is ExportColumnKind.Integer or ExportColumnKind.Coordinate) body.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            if (column.Kind is ExportColumnKind.Date or ExportColumnKind.DateTime or ExportColumnKind.Time) body.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        if (columns.Count > 0)
        {
            var header = sheet.Range(1, 1, 1, columns.Count);
            header.Style.Fill.BackgroundColor = Navy;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Font.Bold = true;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        sheet.Row(1).Height = 22;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            for (var c = 0; c < columns.Count; c++)
                SetCell(sheet.Cell(i + 2, c + 1), columns[c], columns[c].Value(row), info.TimeZone);
        }

        if (columns.Count > 0) sheet.Range(1, 1, Math.Max(1, rows.Count + 1), columns.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);

        WriteInfo(workbook.AddWorksheet("Export info"), columns, rows.Count, info);
        sheet.SetTabActive();
        workbook.SaveAs(output);
    }

    private static void SetCell(IXLCell cell, CustomerExportColumn column, object? value, TimeZoneInfo timeZone)
    {
        switch (value)
        {
            case null:
                return;
            case TimeOnly time:
                cell.SetValue(time.ToTimeSpan());
                return;
            case DateOnly date:
                cell.SetValue(date.ToDateTime(TimeOnly.MinValue));
                return;
            case DateTime utc:
                cell.SetValue(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone));
                return;
            case int number when column.Kind == ExportColumnKind.Integer:
                cell.SetValue(number);
                return;
            case decimal coordinate:
                cell.SetValue(coordinate);
                return;
            case string url when column.Kind == ExportColumnKind.Url:
                cell.SetValue(url);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    cell.SetHyperlink(new XLHyperlink(uri));
                    cell.Style.Font.FontColor = XLColor.FromHtml("#1A56DB");
                    cell.Style.Font.Underline = XLFontUnderlineValues.Single;
                }
                return;
            default:
                // Text columns (and anything unexpected) are written as strings: no number, date or formula conversion.
                cell.SetValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                return;
        }
    }

    private static void WriteInfo(IXLWorksheet sheet, IReadOnlyList<CustomerExportColumn> columns, int count, CustomerExportInfo info)
    {
        sheet.Cell(1, 1).Value = "Fitzobit customer export";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        var lines = new List<(string, string)>
        {
            ("Exported by", info.ExportedBy),
            ("Exported at", $"{TimeZoneInfo.ConvertTimeFromUtc(info.ExportedAtUtc, info.TimeZone):dd MMM yyyy HH:mm} ({info.TimeZone.Id})"),
            ("Rows", info.Scope),
            ("Customers", count.ToString("N0")),
            ("Columns", (info.AllColumns ? "All columns: " : "Visible columns: ") + string.Join(", ", columns.Select(c => c.Header))),
        };
        lines.AddRange(info.Filters.Select(f => (f.Key, f.Value)));

        for (var i = 0; i < lines.Count; i++)
        {
            sheet.Cell(i + 3, 1).Value = lines[i].Item1;
            sheet.Cell(i + 3, 1).Style.Font.Bold = true;
            sheet.Cell(i + 3, 2).SetValue(lines[i].Item2);
            sheet.Cell(i + 3, 2).Style.Alignment.WrapText = true;
        }
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 80;
    }
}
