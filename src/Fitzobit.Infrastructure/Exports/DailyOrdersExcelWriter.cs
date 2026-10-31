using ClosedXML.Excel;
using Fitzobit.Application.Customers;
using Fitzobit.Application.DailyOrders;

namespace Fitzobit.Infrastructure.Exports;

/// <summary>
/// Daily orders as Excel: a "Kitchen totals" sheet (boxes per meal type × preference, then the same per delivery group), then one
/// sheet per delivery group with its totals and its customers in delivery-time order (IDs and phones as text, times as real times).
/// </summary>
public sealed class DailyOrdersExcelWriter : IDailyOrdersExportWriter
{
    private static readonly XLColor Navy = XLColor.FromHtml("#0E1D3D");
    private static readonly XLColor Link = XLColor.FromHtml("#1A56DB");

    public ExportFormat Format => ExportFormat.Excel;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => "xlsx";

    public void Write(Stream output, DailyOrdersDto orders, string exportedBy, DateTime exportedAtUtc, TimeZoneInfo timeZone)
    {
        using var workbook = new XLWorkbook();
        var generated = $"Generated {TimeZoneInfo.ConvertTimeFromUtc(exportedAtUtc, timeZone):dd MMM yyyy, HH:mm} by {exportedBy}";
        WriteTotals(workbook.AddWorksheet("Kitchen totals"), orders, generated);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Kitchen totals" };
        foreach (var group in orders.Groups)
        {
            var name = group.Label.Length > 31 ? group.Label[..31] : group.Label;
            for (var i = 2; !names.Add(name); i++) name = $"{group.Label[..Math.Min(group.Label.Length, 27)]} ({i})";
            WriteGroup(workbook.AddWorksheet(name), orders.Date, group);
        }
        workbook.SaveAs(output);
    }

    private static void WriteTotals(IXLWorksheet sheet, DailyOrdersDto orders, string generated)
    {
        Title(sheet, "Fitzobit — Daily orders", $"{orders.Date:dddd, dd MMM yyyy}");
        sheet.Cell(3, 1).Value = orders.IsDeliveryDay
            ? $"{orders.TotalCustomers} customer(s) · {orders.TotalBoxes} box(es) · {generated}"
            : $"Sunday: no deliveries · {generated}";
        if (!orders.IsDeliveryDay) return;

        var row = 5;
        Header(sheet.Range(row, 1, row, 3), ["Meal type", "Preference", "Boxes"]);
        foreach (var t in orders.Totals)
        {
            row++;
            sheet.Cell(row, 1).Value = t.MealType;
            sheet.Cell(row, 2).Value = t.Preference;
            sheet.Cell(row, 3).Value = t.Boxes;
        }
        row++;
        sheet.Cell(row, 1).Value = "Total";
        sheet.Cell(row, 3).Value = orders.TotalBoxes;
        sheet.Range(row, 1, row, 3).Style.Font.Bold = true;

        // Boxes per delivery group: one row per meal, one column per group.
        row += 2;
        sheet.Cell(row, 1).Value = "By delivery group";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;
        var headers = new List<string> { "Meal type", "Preference" };
        headers.AddRange(orders.Groups.Select(g => g.Label));
        headers.Add("Total");
        Header(sheet.Range(row, 1, row, headers.Count), headers);
        foreach (var t in orders.Totals)
        {
            row++;
            sheet.Cell(row, 1).Value = t.MealType;
            sheet.Cell(row, 2).Value = t.Preference;
            for (var g = 0; g < orders.Groups.Count; g++)
            {
                var boxes = orders.Groups[g].Totals.FirstOrDefault(x => x.MealType == t.MealType && x.Preference == t.Preference)?.Boxes ?? 0;
                if (boxes > 0) sheet.Cell(row, 3 + g).Value = boxes;
            }
            sheet.Cell(row, headers.Count).Value = t.Boxes;
            sheet.Cell(row, headers.Count).Style.Font.Bold = true;
        }
        row++;
        sheet.Cell(row, 1).Value = "Total";
        for (var g = 0; g < orders.Groups.Count; g++) sheet.Cell(row, 3 + g).Value = orders.Groups[g].Boxes;
        sheet.Cell(row, headers.Count).Value = orders.TotalBoxes;
        sheet.Range(row, 1, row, headers.Count).Style.Font.Bold = true;

        sheet.Column(1).Width = 18;
        sheet.Column(2).Width = 14;
        for (var c = 3; c <= headers.Count; c++) sheet.Column(c).Width = 11;
    }

    private static void WriteGroup(IXLWorksheet sheet, DateOnly date, GroupOrdersDto group)
    {
        Title(sheet, $"{group.Label} — {date:dddd, dd MMM yyyy}", $"{group.Customers.Count} customer(s) · {group.Boxes} box(es)");
        sheet.Cell(3, 1).Value = string.Join("  ·  ", group.Totals.Select(t => $"{t.MealType} {t.Preference}: {t.Boxes}"));

        const int head = 5;
        string[] headers = ["Time", "Customer ID", "Customer Name", "Orders", "Boxes", "Phone Number", "Alternative Phone", "Address", "Pincode", "Google Map", "Notes"];
        Header(sheet.Range(head, 1, head, headers.Length), headers);
        sheet.Range(head + 1, 2, Math.Max(head + 1, head + group.Customers.Count), 2).Style.NumberFormat.Format = "@";
        sheet.Range(head + 1, 6, Math.Max(head + 1, head + group.Customers.Count), 7).Style.NumberFormat.Format = "@";
        sheet.Range(head + 1, 9, Math.Max(head + 1, head + group.Customers.Count), 9).Style.NumberFormat.Format = "@";

        var row = head;
        foreach (var c in group.Customers)
        {
            row++;
            if (c.DeliveryTime is { } time)
            {
                sheet.Cell(row, 1).SetValue(time.ToTimeSpan());
                sheet.Cell(row, 1).Style.DateFormat.Format = "hh:mm AM/PM";
            }
            sheet.Cell(row, 2).SetValue(c.CustomerId);
            sheet.Cell(row, 3).SetValue(c.CustomerName);
            sheet.Cell(row, 4).SetValue(c.ItemsText);
            sheet.Cell(row, 5).SetValue(c.Boxes);
            sheet.Cell(row, 6).SetValue(c.PhoneNumber);
            if (c.AlternatePhoneNumber is not null) sheet.Cell(row, 7).SetValue(c.AlternatePhoneNumber);
            if (c.Address is not null) sheet.Cell(row, 8).SetValue(c.Address);
            if (c.Pincode is not null) sheet.Cell(row, 9).SetValue(c.Pincode);
            if (c.GoogleMapUrl is not null && Uri.TryCreate(c.GoogleMapUrl, UriKind.Absolute, out var uri))
            {
                sheet.Cell(row, 10).SetValue("Open map");
                sheet.Cell(row, 10).SetHyperlink(new XLHyperlink(uri));
                sheet.Cell(row, 10).Style.Font.FontColor = Link;
                sheet.Cell(row, 10).Style.Font.Underline = XLFontUnderlineValues.Single;
            }
            if (c.Notes.Count > 0) sheet.Cell(row, 11).SetValue(string.Join("; ", c.Notes));
        }
        sheet.Range(head + 1, 1, Math.Max(head + 1, row), headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        foreach (var wrap in new[] { 4, 8, 11 }) sheet.Column(wrap).Style.Alignment.WrapText = true;

        double[] widths = [10, 14, 22, 34, 7, 15, 15, 45, 9, 11, 40];
        for (var i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];
        sheet.SheetView.FreezeRows(head);
        if (group.Customers.Count > 0) sheet.Range(head, 1, row, headers.Length).SetAutoFilter();
    }

    private static void Title(IXLWorksheet sheet, string title, string subtitle)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(1, 1).Style.Font.FontColor = Navy;
        sheet.Cell(2, 1).Value = subtitle;
        sheet.Cell(2, 1).Style.Font.Bold = true;
    }

    private static void Header(IXLRange range, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) range.Cell(1, i + 1).Value = headers[i];
        range.Style.Fill.BackgroundColor = Navy;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
    }
}
