using Fitzobit.Application.Customers;
using Fitzobit.Application.DailyOrders;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Fitzobit.Infrastructure.Exports;

/// <summary>
/// Daily orders as PDF (A4 landscape): a first page with the kitchen totals and the boxes per delivery group, then each group on a new
/// page with its totals and customers in delivery-time order — one page per delivery person.
/// </summary>
public sealed class DailyOrdersPdfWriter : IDailyOrdersExportWriter
{
    private const double Margin = 36;
    private const double PageWidth = 841.89 - 2 * Margin;
    private static readonly Color Navy = Color.FromRgb(0x0E, 0x1D, 0x3D);
    private static readonly Color Muted = Color.FromRgb(0x5B, 0x67, 0x7D);
    private static readonly Color Rule = Color.FromRgb(0xD5, 0xDB, 0xE5);
    private static readonly Color Stripe = Color.FromRgb(0xF4, 0xF6, 0xFA);
    private static readonly Color LinkBlue = Color.FromRgb(0x1A, 0x56, 0xDB);

    public ExportFormat Format => ExportFormat.Pdf;
    public string ContentType => "application/pdf";
    public string FileExtension => "pdf";

    public void Write(Stream output, DailyOrdersDto orders, string exportedBy, DateTime exportedAtUtc, TimeZoneInfo timeZone)
    {
        PdfFonts.EnsureRegistered();
        var document = new Document();
        document.Info.Title = $"Fitzobit daily orders {orders.Date:dd MMM yyyy}";
        document.Info.Author = exportedBy;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfFonts.Family;
        normal.Font.Size = 9;

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = Orientation.Landscape;
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = Unit.FromPoint(Margin);
        setup.FooterDistance = Unit.FromPoint(16);

        var day = $"{orders.Date:dddd, dd MMM yyyy}";
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.Format.TabStops.AddTabStop(Unit.FromPoint(PageWidth - 1), TabAlignment.Right);
        footer.AddText($"Fitzobit daily orders · {day}");
        footer.AddTab();
        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        Heading(section, "Fitzobit — Daily orders", 15);
        Line(section, orders.IsDeliveryDay ? $"{day} · {orders.TotalCustomers} customer(s) · {orders.TotalBoxes} box(es)" : $"{day} · Sunday: no deliveries", bold: true);
        Line(section, $"Generated {TimeZoneInfo.ConvertTimeFromUtc(exportedAtUtc, timeZone):dd MMM yyyy, HH:mm} by {exportedBy}", muted: true).Format.SpaceAfter = 10;

        if (orders.IsDeliveryDay && orders.TotalBoxes > 0)
        {
            Heading(section, "Kitchen totals", 11);
            var totals = NewTable(section, [140, 110, 60]);
            HeaderRow(totals, ["Meal type", "Preference", "Boxes"]);
            foreach (var t in orders.Totals) Row(totals, [t.MealType, t.Preference, t.Boxes.ToString()]);
            Bold(Row(totals, ["Total", "", orders.TotalBoxes.ToString()]));

            section.AddParagraph().Format.SpaceAfter = 6;
            Heading(section, "By delivery group", 11);
            var columns = new List<double> { 120, 90 };
            var groupWidth = Math.Max(42, Math.Min(70, (PageWidth - 210 - 55) / Math.Max(1, orders.Groups.Count)));
            columns.AddRange(orders.Groups.Select(_ => groupWidth));
            columns.Add(55);
            var matrix = NewTable(section, columns);
            HeaderRow(matrix, ["Meal type", "Preference", .. orders.Groups.Select(g => g.Label), "Total"]);
            foreach (var t in orders.Totals)
                Row(matrix, [t.MealType, t.Preference,
                    .. orders.Groups.Select(g => g.Totals.FirstOrDefault(x => x.MealType == t.MealType && x.Preference == t.Preference)?.Boxes is { } b ? b.ToString() : ""),
                    t.Boxes.ToString()]);
            Bold(Row(matrix, ["Total", "", .. orders.Groups.Select(g => g.Boxes.ToString()), orders.TotalBoxes.ToString()]));
        }
        else if (orders.IsDeliveryDay)
        {
            Line(section, "No deliveries on this day.", muted: true);
        }

        foreach (var group in orders.Groups)
        {
            section.AddPageBreak();
            Heading(section, $"{group.Label} — {day}", 13);
            Line(section, $"{group.Customers.Count} customer(s) · {group.Boxes} box(es)", bold: true);
            Line(section, string.Join("   ·   ", group.Totals.Select(t => $"{t.MealType} {t.Preference}: {t.Boxes}")), muted: true).Format.SpaceAfter = 8;

            var table = NewTable(section, [48, 118, 150, 36, 82, 210, PageWidth - 644]);
            HeaderRow(table, ["Time", "Customer", "Orders", "Boxes", "Phone", "Address", "Notes"]);
            foreach (var c in group.Customers)
            {
                var row = table.AddRow();
                if (table.Rows.Count % 2 == 1) row.Shading.Color = Stripe;
                row.Cells[0].AddParagraph(c.DeliveryTime?.ToString("hh:mm tt") ?? "—");
                var who = row.Cells[1].AddParagraph();
                who.AddFormattedText(c.CustomerId, TextFormat.Bold);
                who.AddLineBreak();
                who.AddText(c.CustomerName);
                var items = row.Cells[2].AddParagraph();
                for (var i = 0; i < c.Items.Count; i++)
                {
                    if (i > 0) items.AddLineBreak();
                    items.AddText(c.Items[i].Text);
                }
                if (c.PausedDays > 0)
                {
                    var paused = row.Cells[2].AddParagraph($"Paused: {c.PausedText}");
                    paused.Format.Font.Size = 7;
                    paused.Format.Font.Color = Muted;
                }
                row.Cells[3].AddParagraph(c.Boxes.ToString()).Format.Alignment = ParagraphAlignment.Right;
                var phone = row.Cells[4].AddParagraph(c.PhoneNumber);
                if (c.AlternatePhoneNumber is not null)
                {
                    phone.AddLineBreak();
                    phone.AddText(c.AlternatePhoneNumber);
                }
                var address = row.Cells[5].AddParagraph(Clean(c.Address) + (c.Pincode is null ? "" : $" — {c.Pincode}"));
                if (c.GoogleMapUrl is not null)
                {
                    address.AddLineBreak();
                    var link = address.AddHyperlink(c.GoogleMapUrl, HyperlinkType.Web);
                    link.Font.Color = LinkBlue;
                    link.AddText("Open in Google Maps");
                }
                row.Cells[6].AddParagraph(Clean(string.Join("; ", c.Notes)));
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    private static void Heading(Section section, string text, double size)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = size;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = Navy;
        p.Format.SpaceAfter = 3;
    }

    private static Paragraph Line(Section section, string text, bool bold = false, bool muted = false)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = 9;
        p.Format.Font.Bold = bold;
        if (muted) p.Format.Font.Color = Muted;
        return p;
    }

    private static Table NewTable(Section section, IReadOnlyList<double> widths)
    {
        var table = section.AddTable();
        table.Borders.Color = Rule;
        table.Borders.Width = 0.5;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(4);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(2.5);
        foreach (var w in widths) table.AddColumn(Unit.FromPoint(w));
        return table;
    }

    private static void HeaderRow(Table table, IReadOnlyList<string> headers)
    {
        var row = table.AddRow();
        row.HeadingFormat = true; // repeated on every page
        row.Shading.Color = Navy;
        row.Format.Font.Bold = true;
        row.Format.Font.Color = Colors.White;
        for (var i = 0; i < headers.Count; i++) row.Cells[i].AddParagraph(headers[i]);
    }

    private static Row Row(Table table, IReadOnlyList<string> values)
    {
        var row = table.AddRow();
        if (table.Rows.Count % 2 == 1) row.Shading.Color = Stripe;
        for (var i = 0; i < values.Count; i++) row.Cells[i].AddParagraph(values[i]);
        return row;
    }

    private static void Bold(Row row) => row.Format.Font.Bold = true;

    private static string Clean(string? text) =>
        string.IsNullOrEmpty(text) ? "" : new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
}
