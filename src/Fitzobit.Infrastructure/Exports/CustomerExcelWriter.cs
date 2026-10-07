using ClosedXML.Excel;
using Fitzobit.Application.Customers;

namespace Fitzobit.Infrastructure.Exports;

/// <summary>
/// Writes the customer export: formatted header, autofilter, frozen header row, sized columns, IDs/phones/pincodes as
/// text (never numbers or scientific notation), real date cells, and an information sheet describing the export.
/// </summary>
public sealed class CustomerExcelWriter : ICustomerExcelWriter
{
    private static readonly XLColor Navy = XLColor.FromHtml("#0E1D3D");

    private static readonly (string Header, double Width)[] Columns =
    [
        ("CustomerID", 16), ("CustomerName", 28), ("PhoneNumber", 17), ("Location", 52), ("Pincode", 10),
        ("Plan", 46), ("DeliveryGroup", 26), ("Status", 11), ("CreatedAt", 19), ("UpdatedAt", 19)
    ];

    public void Write(Stream output, IReadOnlyList<CustomerExportRow> rows, CustomerExportInfo info)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Customers");

        for (var c = 0; c < Columns.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Columns[c].Header;
            sheet.Column(c + 1).Width = Columns[c].Width;
        }
        var header = sheet.Range(1, 1, 1, Columns.Length);
        header.Style.Fill.BackgroundColor = Navy;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        sheet.Row(1).Height = 22;

        var lastRow = Math.Max(2, rows.Count + 1);
        // Text columns: set the format before writing so values stay exactly as stored.
        foreach (var col in new[] { 1, 3, 5 })
            sheet.Range(2, col, lastRow, col).Style.NumberFormat.Format = "@";
        sheet.Range(2, 9, lastRow, 10).Style.DateFormat.Format = "dd-mmm-yyyy hh:mm";

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = i + 2;
            sheet.Cell(row, 1).SetValue(r.CustomerId);
            sheet.Cell(row, 2).SetValue(r.CustomerName);
            sheet.Cell(row, 3).SetValue(r.PhoneNumber);
            sheet.Cell(row, 4).SetValue(r.Location ?? "");
            sheet.Cell(row, 5).SetValue(r.Pincode ?? "");
            sheet.Cell(row, 6).SetValue(r.Plan ?? "");
            sheet.Cell(row, 7).SetValue(r.DeliveryGroup ?? "");
            sheet.Cell(row, 8).SetValue(r.Status);
            sheet.Cell(row, 9).SetValue(TimeZoneInfo.ConvertTimeFromUtc(r.CreatedAt, info.TimeZone));
            sheet.Cell(row, 10).SetValue(TimeZoneInfo.ConvertTimeFromUtc(r.UpdatedAt, info.TimeZone));
        }

        sheet.Range(1, 1, Math.Max(1, rows.Count + 1), Columns.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.Range(2, 4, lastRow, 4).Style.Alignment.WrapText = false;

        WriteInfo(workbook.AddWorksheet("Export info"), rows.Count, info);
        sheet.SetTabActive();
        workbook.SaveAs(output);
    }

    private static void WriteInfo(IXLWorksheet sheet, int count, CustomerExportInfo info)
    {
        sheet.Cell(1, 1).Value = "Fitzobit customer export";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        var lines = new List<(string, string)>
        {
            ("Exported by", info.ExportedBy),
            ("Exported at", $"{TimeZoneInfo.ConvertTimeFromUtc(info.ExportedAtUtc, info.TimeZone):dd-MMM-yyyy HH:mm} ({info.TimeZone.Id})"),
            ("Scope", info.Scope),
            ("Customers", count.ToString("N0")),
        };
        lines.AddRange(info.Filters.Select(f => (f.Key, f.Value)));

        for (var i = 0; i < lines.Count; i++)
        {
            sheet.Cell(i + 3, 1).Value = lines[i].Item1;
            sheet.Cell(i + 3, 1).Style.Font.Bold = true;
            sheet.Cell(i + 3, 2).SetValue(lines[i].Item2);
        }
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 60;
    }
}
