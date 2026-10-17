using System.Globalization;
using Fitzobit.Application.Customers;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;

namespace Fitzobit.Infrastructure.Exports;

/// <summary>
/// Writes the customer export as a printable A4 PDF: heading with the export's scope and filters, a table with exactly the
/// chosen columns whose header row repeats on every page, page numbers and the generation time in the footer.
/// Column widths come from the measured text; the page turns to landscape and the font gets smaller only when needed,
/// and long names/addresses wrap instead of being cut off.
/// </summary>
public sealed class CustomerPdfWriter : ICustomerExportWriter
{
    private const double Margin = 36; // points (12.7 mm)
    private const double MaxWrapWidth = 230;
    private const double MinWrapWidth = 40;
    private const int MeasuredRows = 1500;
    private const char NoBreak = '\u00A0';
    private static readonly double[] FontSizes = [9, 8, 7.2, 6.5];

    private static readonly Color Navy = Color.FromRgb(0x0E, 0x1D, 0x3D);
    private static readonly Color Muted = Color.FromRgb(0x5B, 0x67, 0x7D);
    private static readonly Color Rule = Color.FromRgb(0xD5, 0xDB, 0xE5);
    private static readonly Color Stripe = Color.FromRgb(0xF4, 0xF6, 0xFA);

    public ExportFormat Format => ExportFormat.Pdf;
    public string ContentType => "application/pdf";
    public string FileExtension => "pdf";

    public void Write(Stream output, IReadOnlyList<CustomerExportColumn> columns, IReadOnlyList<CustomerExportRow> rows, CustomerExportInfo info)
    {
        PdfFonts.EnsureRegistered();
        var cells = rows.Select(r => columns.Select(c => FormatValue(c.Value(r), info.TimeZone)).ToArray()).ToList();
        var layout = Layout(columns, cells);

        var document = new Document();
        document.Info.Title = "Fitzobit customer export";
        document.Info.Author = info.ExportedBy;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfFonts.Family;
        normal.Font.Size = 9;

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = layout.Landscape ? Orientation.Landscape : Orientation.Portrait;
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = Unit.FromPoint(Margin);
        setup.FooterDistance = Unit.FromPoint(16);

        var generated = $"{TimeZoneInfo.ConvertTimeFromUtc(info.ExportedAtUtc, info.TimeZone):dd MMM yyyy, HH:mm} ({info.TimeZone.Id})";
        WriteHeading(section, info, rows.Count, columns, generated);
        WriteFooter(section, generated);
        WriteTable(section, columns, cells, layout);
        if (rows.Count == 0)
        {
            var empty = section.AddParagraph("No customers match this export.");
            empty.Format.SpaceBefore = Unit.FromPoint(10);
            empty.Format.Font.Color = Muted;
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
    }

    private static void WriteHeading(Section section, CustomerExportInfo info, int count, IReadOnlyList<CustomerExportColumn> columns, string generated)
    {
        var title = section.AddParagraph("Fitzobit — Customers");
        title.Format.Font.Size = 15;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Navy;
        title.Format.SpaceAfter = Unit.FromPoint(3);

        var summary = section.AddParagraph(
            $"{info.Scope} · {count:N0} customer{(count == 1 ? "" : "s")} · {(info.AllColumns ? "All columns" : $"{columns.Count} visible column{(columns.Count == 1 ? "" : "s")}")}");
        summary.Format.Font.Size = 9.5;
        summary.Format.Font.Bold = true;

        var filters = info.Filters.Select(f => $"{f.Key}: {f.Value}").ToList();
        var filterLine = section.AddParagraph(string.Join("   ·   ", filters));
        filterLine.Format.Font.Size = 8.5;
        filterLine.Format.Font.Color = Muted;

        var by = section.AddParagraph($"Generated {generated} by {Clean(info.ExportedBy)}");
        by.Format.Font.Size = 8.5;
        by.Format.Font.Color = Muted;
        by.Format.SpaceAfter = Unit.FromPoint(10);
    }

    private static void WriteFooter(Section section, string generated)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        var width = section.PageSetup.Orientation == Orientation.Landscape ? 841.89 : 595.27;
        footer.Format.TabStops.AddTabStop(Unit.FromPoint(width - 2 * Margin - 1), TabAlignment.Right);
        footer.AddText($"Fitzobit customer export · Generated {generated}");
        footer.AddTab();
        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void WriteTable(Section section, IReadOnlyList<CustomerExportColumn> columns, List<string?[]> cells, PdfLayout layout)
    {
        var table = section.AddTable();
        table.Borders.Color = Rule;
        table.Borders.Width = 0.5;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(Padding(layout.FontSize));
        table.TopPadding = table.BottomPadding = Unit.FromPoint(2.2);
        table.Format.Font.Size = layout.FontSize;

        for (var c = 0; c < columns.Count; c++)
        {
            var column = table.AddColumn(Unit.FromPoint(layout.Widths[c]));
            column.Format.Alignment = IsNumeric(columns[c]) ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var header = table.AddRow();
        header.HeadingFormat = true; // repeated at the top of every page
        header.Shading.Color = Navy;
        header.Format.Font.Bold = true;
        header.Format.Font.Color = Colors.White;
        header.VerticalAlignment = VerticalAlignment.Center;
        for (var c = 0; c < columns.Count; c++) header.Cells[c].AddParagraph(columns[c].Header);

        for (var i = 0; i < cells.Count; i++)
        {
            var row = table.AddRow();
            if (i % 2 == 1) row.Shading.Color = Stripe;
            for (var c = 0; c < columns.Count; c++)
            {
                var paragraph = row.Cells[c].AddParagraph(cells[i][c] ?? "—");
                if (cells[i][c] is null) paragraph.Format.Font.Color = Muted;
            }
        }
    }

    private sealed record PdfLayout(bool Landscape, double FontSize, double[] Widths);

    /// <summary>
    /// Chooses orientation, font size and column widths. Columns that do not wrap (IDs, phones, dates, numbers) get their full
    /// width; wrapping columns (names, addresses) share the rest but never get narrower than their longest word.
    /// </summary>
    private static PdfLayout Layout(IReadOnlyList<CustomerExportColumn> columns, List<string?[]> cells)
    {
        var portrait = 595.27 - 2 * Margin;
        var landscape = 841.88 - 2 * Margin;
        if (columns.Count == 0) return new PdfLayout(false, FontSizes[0], []);
        var sample = cells.Count <= MeasuredRows ? cells : cells.Where((_, i) => i % (cells.Count / MeasuredRows + 1) == 0).ToList();

        using var gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        PdfLayout? fallback = null;
        foreach (var size in FontSizes)
        {
            var padding = Padding(size);
            var font = new XFont(PdfFonts.Family, size, XFontStyleEx.Regular);
            var bold = new XFont(PdfFonts.Family, size, XFontStyleEx.Bold);
            var want = new double[columns.Count];
            var min = new double[columns.Count];
            for (var c = 0; c < columns.Count; c++)
            {
                double widest = 0, longestWord = 0;
                foreach (var row in sample)
                {
                    var text = row[c] ?? "—";
                    widest = Math.Max(widest, Measure(gfx, text, font));
                    if (columns[c].Wraps)
                        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            longestWord = Math.Max(longestWord, Measure(gfx, word, font));
                }
                var headerWords = columns[c].Header.Split(' ').Max(w => Measure(gfx, w, bold));
                var headerFull = Measure(gfx, columns[c].Header, bold);
                if (columns[c].Wraps)
                {
                    want[c] = Math.Min(MaxWrapWidth, Math.Max(widest, headerFull)) + 2 * padding;
                    min[c] = Math.Min(want[c], Math.Max(Math.Max(longestWord, headerWords) + 2 * padding, MinWrapWidth));
                }
                else
                {
                    // Header text may wrap onto two lines; the values themselves always stay on one line.
                    min[c] = want[c] = Math.Max(widest, headerWords) + 2 * padding;
                    want[c] = Math.Max(want[c], Math.Min(headerFull + 2 * padding, want[c] * 1.6));
                }
            }

            // Portrait unless the table needs more room; a few columns stay portrait and wrap if that is enough.
            var isLandscape = want.Sum() > portrait && (columns.Count > 4 || min.Sum() > portrait);
            var available = isLandscape ? landscape : portrait;
            if (min.Sum() > available)
            {
                fallback = new PdfLayout(true, size, Scale(min, landscape / min.Sum()));
                continue; // try a smaller font
            }

            var layout = new PdfLayout(isLandscape, size, Distribute(columns, want, min, available));
            // Accept this font size unless names/addresses would be squeezed into narrow, many-line cells.
            var wrapWant = columns.Select((c, i) => c.Wraps ? want[i] : 0).Sum();
            var wrapGot = columns.Select((c, i) => c.Wraps ? layout.Widths[i] : 0).Sum();
            fallback = layout;
            if (wrapWant == 0 || wrapGot / wrapWant >= 0.6) return layout;
        }
        // The smallest font that fits (very wide full exports): any remaining overflow is shared proportionally.
        return fallback!;
    }

    /// <summary>
    /// Every column gets its minimum; spare width goes first to wrapping columns (so they need fewer lines), then to headers
    /// that would otherwise wrap; when everything fits, the table is widened to the full page width.
    /// </summary>
    private static double[] Distribute(IReadOnlyList<CustomerExportColumn> columns, double[] want, double[] min, double available)
    {
        if (want.Sum() <= available) return Scale(want, available / want.Sum());
        var widths = (double[])min.Clone();
        foreach (var wraps in new[] { true, false })
        {
            var spare = available - widths.Sum();
            var growth = widths.Select((w, i) => columns[i].Wraps == wraps ? want[i] - w : 0).ToArray();
            var total = growth.Sum();
            if (spare <= 0 || total <= 0) continue;
            var share = Math.Min(1, spare / total);
            for (var i = 0; i < widths.Length; i++) widths[i] += growth[i] * share;
        }
        return widths;
    }

    /// <summary>Text width with a small safety margin (the renderer's line breaking needs slightly more than the exact width).</summary>
    private static double Measure(XGraphics gfx, string text, XFont font) => gfx.MeasureString(text, font).Width * 1.03 + 1.5;

    private static double Padding(double fontSize) => Math.Round(fontSize * 0.38, 1);

    private static double[] Scale(double[] widths, double factor) => widths.Select(w => w * factor).ToArray();

    private static bool IsNumeric(CustomerExportColumn column) => column.Kind is ExportColumnKind.Integer or ExportColumnKind.Coordinate;

    private static string? FormatValue(object? value, TimeZoneInfo timeZone) => value switch
    {
        null => null,
        TimeOnly time => time.ToString("hh:mm tt", CultureInfo.InvariantCulture).Replace(' ', NoBreak),
        DateOnly date => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).Replace(' ', NoBreak),
        DateTime utc => LocalDateTime(utc, timeZone),
        decimal coordinate => coordinate.ToString("0.000000", CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        string text when text.Length == 0 => null,
        _ => Clean(Convert.ToString(value, CultureInfo.InvariantCulture))
    };

    /// <summary>"08 Oct 2026 17:11" in the export time zone; the date never breaks, the time may move to a second line.</summary>
    private static string LocalDateTime(DateTime utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
        return local.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).Replace(' ', NoBreak) + " " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Line breaks and other control characters in stored text become spaces (the table wraps text itself).</summary>
    private static string Clean(string? text) =>
        string.IsNullOrEmpty(text) ? "" : new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
}
