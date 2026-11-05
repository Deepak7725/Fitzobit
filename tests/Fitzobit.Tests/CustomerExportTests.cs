using ClosedXML.Excel;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Infrastructure.Exports;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;

namespace Fitzobit.Tests;

/// <summary>
/// Excel and PDF exports: rows come from the server with exactly the Customers screen's filters/sort/page, files contain only
/// the chosen columns, and both formats receive the same rows and columns.
/// </summary>
public class CustomerExportTests
{
    /// <summary>Records what a format writer was asked to write.</summary>
    private sealed class CapturingWriter(ExportFormat format) : ICustomerExportWriter
    {
        public ExportFormat Format => format;
        public string ContentType => "application/test";
        public string FileExtension => "test";
        public List<string> Headers { get; } = [];
        public List<string> CustomerIds { get; } = [];
        public List<object?[]> Values { get; } = [];

        public void Write(Stream output, IReadOnlyList<CustomerExportColumn> columns, IReadOnlyList<CustomerExportRow> rows, CustomerExportInfo info)
        {
            Headers.AddRange(columns.Select(c => c.Header));
            CustomerIds.AddRange(rows.Select(r => r.CustomerId));
            Values.AddRange(rows.Select(r => columns.Select(c => c.Value(r)).ToArray()));
        }
    }

    private static CustomerExportService Service(TestDatabase test, FitzobitDbContext db, ExportOptions? options = null, params ICustomerExportWriter[] writers) =>
        new(db, test.Queries(db), writers.Length > 0 ? writers : [new CustomerExcelWriter(), new CustomerPdfWriter()], test.AuditFor(db), test.User,
            test.Clock, Options.Create(options ?? new ExportOptions()));

    private static async Task<(CapturingWriter Excel, CapturingWriter Pdf)> BothAsync(TestDatabase test, CustomerExportRequest request)
    {
        var (excel, pdf) = (new CapturingWriter(ExportFormat.Excel), new CapturingWriter(ExportFormat.Pdf));
        await using var db = test.CreateContext();
        request.Format = ExportFormat.Excel;
        await Service(test, db, null, excel, pdf).ExportAsync(request);
        request.Format = ExportFormat.Pdf;
        await Service(test, db, null, excel, pdf).ExportAsync(request);
        return (excel, pdf);
    }

    [Fact]
    public async Task Active_group_2_with_three_columns_exports_only_those_rows_and_columns_in_both_formats()
    {
        using var test = new TestDatabase();
        var data = await CustomerViewDataTests.SeedAsync(test);
        var filter = new CustomerFilter { StatusId = test.Entry(DictionaryType.CustomerStatus, "ACTIVE"), Group = data.SouthId.ToString() };
        var request = new CustomerExportRequest { Scope = ExportScope.Filtered, Filter = filter, Columns = ["customerName", "phone", "group"] };

        await using (var db = test.CreateContext())
        {
            var file = await Service(test, db).ExportAsync(request);
            Assert.Equal(15, file.RowCount);
            Assert.EndsWith(".xlsx", file.FileName);
            using var workbook = new XLWorkbook(new MemoryStream(file.Content));
            var sheet = workbook.Worksheet("Customers");
            Assert.Equal(3, sheet.LastColumnUsed()!.ColumnNumber());
            Assert.Equal(["Customer Name", "Phone Number", "Group Number"], sheet.Row(1).Cells(1, 3).Select(c => c.GetString()));
            Assert.Equal(16, sheet.LastRowUsed()!.RowNumber());
            Assert.All(Enumerable.Range(2, 15), r => Assert.Equal(2.0, sheet.Cell(r, 3).GetDouble()));
            Assert.Equal(("Customer 21", "9800000021"), (sheet.Cell(2, 1).GetString(), sheet.Cell(2, 2).GetString()));
            Assert.Equal("A1:C16", sheet.AutoFilter.Range.RangeAddress.ToString());

            request.Format = ExportFormat.Pdf;
            var pdf = await Service(test, db).ExportAsync(request);
            Assert.Equal(("application/pdf", 15), (pdf.ContentType, pdf.RowCount));
            Assert.EndsWith(".pdf", pdf.FileName);
            using var document = PdfReader.Open(new MemoryStream(pdf.Content), PdfDocumentOpenMode.Import);
            Assert.Equal(1, document.PageCount);
            Assert.True(document.Pages[0].Height.Point > document.Pages[0].Width.Point, "three columns fit on a portrait page");
        }

        var (excel, pdfWriter) = await BothAsync(test, request);
        Assert.Equal(["Customer Name", "Phone Number", "Group Number"], excel.Headers);
        Assert.Equal(excel.Headers, pdfWriter.Headers);
        Assert.Equal(Enumerable.Range(21, 15).Select(i => $"CB{i:000}"), excel.CustomerIds);
        Assert.Equal(excel.CustomerIds, pdfWriter.CustomerIds);
        Assert.Equal(excel.Values.Select(v => string.Join("|", v)), pdfWriter.Values.Select(v => string.Join("|", v)));
    }

    [Fact]
    public async Task Filtered_exports_match_the_server_side_list_for_every_kind_of_filter()
    {
        using var test = new TestDatabase();
        var data = await CustomerViewDataTests.SeedAsync(test);
        var weekly = test.Entry(DictionaryType.Frequency, "WEEKLY");
        var filters = new CustomerFilter[]
        {
            new() { Search = "Customer 2" },
            new() { Search = "group 1", Sort = "customerName", Desc = true },
            new() { Pincode = "641002" },
            new() { MealTypeId = data.MediumId },
            new() { FrequencyId = weekly },
            new() { Pincode = "641018", MealTypeId = data.MediumId, FrequencyId = weekly, Group = data.NorthId.ToString(), Sort = "endDate" },
            new() { Group = "none", StatusId = data.InactiveId },
        };

        foreach (var filter in filters)
        {
            await using var db = test.CreateContext();
            var expected = (await test.Queries(db).ListAsync(filter, 1, 100)).Items.Select(i => i.CustomerId).ToList();
            Assert.NotEmpty(expected);
            var (excel, pdf) = await BothAsync(test, new CustomerExportRequest { Scope = ExportScope.Filtered, Filter = filter, Columns = ["customerId", "pincode"] });
            Assert.Equal(expected, excel.CustomerIds);
            Assert.Equal(expected, pdf.CustomerIds);
            Assert.Equal(["Customer ID", "Pincode"], pdf.Headers);
        }
    }

    [Fact]
    public async Task Selected_rows_and_current_page_scopes()
    {
        using var test = new TestDatabase();
        var data = await CustomerViewDataTests.SeedAsync(test);

        var selected = new CustomerExportRequest
        {
            Scope = ExportScope.Selected,
            Ids = [data.Ids["CB039"], data.Ids["CB003"], data.Ids["CB003"]],
            Filter = new CustomerFilter { Pincode = "999999", Sort = "customerId", Desc = true }, // filters do not apply to a selection
            Columns = ["status", "customerId"]
        };
        var (excel, pdf) = await BothAsync(test, selected);
        Assert.Equal(["CB039", "CB003"], excel.CustomerIds);
        Assert.Equal(excel.CustomerIds, pdf.CustomerIds);
        Assert.Equal(["Status", "Customer ID"], pdf.Headers);

        var filter = new CustomerFilter { Pincode = "641002", Sort = "customerName", Desc = true };
        await using var db = test.CreateContext();
        var page2 = (await test.Queries(db).ListAsync(filter, 2, 10)).Items.Select(i => i.CustomerId).ToList();
        var (pageExcel, pagePdf) = await BothAsync(test, new CustomerExportRequest { Scope = ExportScope.Page, Filter = filter, Page = 2, PageSize = 10 });
        Assert.Equal(page2, pageExcel.CustomerIds);
        Assert.Equal(page2, pagePdf.CustomerIds);
        Assert.Equal(CustomerExportColumns.All.Count, pagePdf.Headers.Count); // no columns chosen = full export

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(test, db).ExportAsync(new CustomerExportRequest { Scope = ExportScope.Selected, Ids = [] }));
    }

    [Fact]
    public async Task All_scope_ignores_filters_and_is_audited_with_format_and_columns()
    {
        using var test = new TestDatabase();
        await CustomerViewDataTests.SeedAsync(test);
        await using var db = test.CreateContext();

        var file = await Service(test, db).ExportAsync(new CustomerExportRequest
            { Scope = ExportScope.All, Format = ExportFormat.Pdf, Filter = new CustomerFilter { Pincode = "641002" }, Columns = ["customerId"] });
        Assert.Equal(40, file.RowCount);
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.ExportPerformed);
        Assert.Contains("\"Format\":\"Pdf\"", audit.Details);
        Assert.Contains("\"Columns\":[\"Customer ID\"]", audit.Details);
    }

    [Fact]
    public async Task Unknown_or_repeated_columns_and_oversized_pdfs_are_rejected()
    {
        using var test = new TestDatabase();
        await CustomerViewDataTests.SeedAsync(test);
        await using var db = test.CreateContext();
        var service = Service(test, db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ExportAsync(new CustomerExportRequest { Columns = ["customerId", "password"] }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ExportAsync(new CustomerExportRequest { Columns = ["phone", "PHONE"] }));

        var small = Service(test, db, new ExportOptions { PdfMaxRows = 10 });
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => small.ExportAsync(new CustomerExportRequest { Format = ExportFormat.Pdf }));
        Assert.Contains("Excel exports allow more rows", error.Message);
        Assert.Equal(40, (await small.ExportAsync(new CustomerExportRequest { Format = ExportFormat.Excel })).RowCount);
    }

    [Fact]
    public async Task Alternative_phone_and_notes_are_exported_as_text()
    {
        using var test = new TestDatabase();
        int id;
        await using (var db = test.CreateContext())
        {
            var request = test.NewCustomer(note: "Gate code 1234\nCall before delivery");
            request.AlternatePhoneNumber = "09876500000";
            id = (await test.Commands(db).CreateAsync(request)).Id;
        }
        test.Clock.UtcNow = test.Clock.UtcNow.AddDays(1);
        await using (var db = test.CreateContext()) await test.Notes(db).AddAsync(id, new SaveNoteRequest("Less spice"));

        await using var check = test.CreateContext();
        var file = await Service(test, check).ExportAsync(new CustomerExportRequest { Scope = ExportScope.All, Columns = ["customerId", "alternatePhone", "notes"] });

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("Customers");
        Assert.Equal(["Customer ID", "Alternative Phone", "Notes"], sheet.Row(1).Cells(1, 3).Select(c => c.GetString()));
        Assert.Equal("09876500000", sheet.Cell(2, 2).GetString()); // the leading 0 stays: a text cell
        Assert.Equal("08 Oct 2026, test-user: Less spice; 07 Oct 2026, test-user: Gate code 1234 Call before delivery", sheet.Cell(2, 3).GetString());
    }

    [Fact]
    public async Task The_google_maps_link_is_a_clickable_link_in_excel_and_pdf()
    {
        const string url = "https://www.google.com/maps/place/Government+Raja+Mirasdar+Hospital/@10.7846186,79.1364729,17z";
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            var request = test.NewCustomer();
            request.GoogleMapUrl = url;
            await test.Commands(db).CreateAsync(request);
            await test.Commands(db).CreateAsync(test.NewCustomer("Karthik N", "9123456780")); // no link
        }

        await using var check = test.CreateContext();
        var columns = new List<string> { "customerId", "googleMap" };
        var excel = await Service(test, check).ExportAsync(new CustomerExportRequest { Scope = ExportScope.All, Columns = columns, Filter = new CustomerFilter { Sort = "customerId" } });
        using var workbook = new XLWorkbook(new MemoryStream(excel.Content));
        var sheet = workbook.Worksheet("Customers");
        Assert.Equal("Google Map", sheet.Cell(1, 2).GetString());
        Assert.Equal(url, sheet.Cell(2, 2).GetString());
        Assert.True(sheet.Cell(2, 2).HasHyperlink);
        Assert.Equal(new Uri(url), sheet.Cell(2, 2).GetHyperlink().ExternalAddress);
        Assert.True(sheet.Cell(3, 2).IsEmpty());

        var pdf = await Service(test, check).ExportAsync(new CustomerExportRequest { Scope = ExportScope.All, Format = ExportFormat.Pdf, Columns = columns });
        var raw = System.Text.Encoding.Latin1.GetString(pdf.Content);
        Assert.Contains("/URI", raw); // a clickable web link annotation
    }

    [Fact]
    public async Task Excel_keeps_ids_phones_and_pincodes_as_text_and_dates_as_dates()
    {
        using var test = new TestDatabase();
        var data = await CustomerViewDataTests.SeedAsync(test);
        await using var db = test.CreateContext();

        var filter = new CustomerFilter { Group = data.NorthId.ToString(), MealTypeId = data.MediumId, Pincode = "641002", Sort = "customerId", Desc = true };
        var file = await Service(test, db).ExportAsync(new CustomerExportRequest { Scope = ExportScope.Filtered, Filter = filter });

        Assert.Equal(3, file.RowCount);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("Customers");
        var headers = CustomerExportColumns.All.Select(c => c.Header).ToList();
        Assert.Equal(headers, sheet.Row(1).Cells(1, headers.Count).Select(c => c.GetString()));
        int Col(string header) => headers.IndexOf(header) + 1;

        Assert.Equal(["CB018", "CB012", "CB006"], Enumerable.Range(2, 3).Select(r => sheet.Cell(r, Col("Customer ID")).GetString()));
        Assert.Equal(("Veg Medium ×1 · Weekly (12 Oct 2026 – 17 Oct 2026)", 1.0),
            (sheet.Cell(2, Col("Packages")).GetString(), sheet.Cell(2, Col("Group Number")).GetDouble()));
        Assert.Equal(XLDataType.TimeSpan, sheet.Cell(2, Col("Delivery Time")).DataType);
        Assert.Equal(new TimeSpan(12, 0, 0), sheet.Cell(2, Col("Delivery Time")).GetTimeSpan()); // CB018 delivers at 6 + 18 % 12 = 12:00
        foreach (var text in new[] { "Customer ID", "Phone Number", "Pincode" })
        {
            Assert.Equal(XLDataType.Text, sheet.Cell(2, Col(text)).DataType);
            Assert.Equal("@", sheet.Cell(2, Col(text)).Style.NumberFormat.Format);
        }
        Assert.Equal(("9800000018", "641002"), (sheet.Cell(2, Col("Phone Number")).GetString(), sheet.Cell(2, Col("Pincode")).GetString()));
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, Col("Start Date")).DataType);
        Assert.Equal(new DateTime(2026, 10, 12), sheet.Cell(2, Col("Start Date")).GetDateTime());
        Assert.Equal(new DateTime(2026, 10, 17), sheet.Cell(2, Col("End Date")).GetDateTime());
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, Col("Created At")).DataType);
        Assert.Equal("Looking up", sheet.Cell(2, Col("Location")).GetString()); // new customers wait for a lookup
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.Contains(workbook.Worksheet("Export info").CellsUsed(), c => c.GetString() == "Medium");
    }

    [Fact]
    public void Pdf_with_every_column_turns_landscape_and_repeats_over_several_pages()
    {
        var rows = Enumerable.Range(1, 120).Select(i => new CustomerExportRow($"FITZ-{i:000000}", $"Customer with a fairly long name {i}", $"98{i:00000000}",
            i % 2 == 0 ? $"97{i:00000000}" : null, $"{i} Thiruvenkatasamy Road West, R.S. Puram, Coimbatore, Tamil Nadu 641002", "641002",
            i % 4 == 0 ? $"https://www.google.com/maps/place/Customer+{i}/@10.7867{i:000},79.1378{i:000},17z/data=!3m1!4b1" : null, new TimeOnly(7, 30),
            "With Egg Meal bowl ×1 · Monthly (12 Oct 2026 – 11 Nov 2026); Veg Oats ×2 · Weekly (13 Oct 2026 – 19 Oct 2026)",
            i % 3 == 0 ? "08 Oct 2026, Test Admin: Gate code 1234, call before delivery" : null, new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 11),
            i % 2 == 0 ? "5 (2 own + 3 for everyone)" : "0", "Active", i % 3 == 0 ? null : i % 5 + 1, GeocodeStatus.Located,
            GeoMatchLevel.Locality, 11.0123456m, 76.9512345m, new DateTime(2026, 10, 8, 4, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 8, 5, 30, 0, DateTimeKind.Utc))).ToList();
        var info = new CustomerExportInfo("All customers", [new("Filters", "None (all customers)")], true, "Test Admin",
            new DateTime(2026, 10, 8, 6, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        using var stream = new MemoryStream();
        new CustomerPdfWriter().Write(stream, CustomerExportColumns.All, rows, info);
        using var document = PdfReader.Open(new MemoryStream(stream.ToArray()), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 2);
        Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(), p => Assert.True(p.Width.Point > p.Height.Point, "landscape"));

        using var empty = new MemoryStream();
        new CustomerPdfWriter().Write(empty, CustomerExportColumns.Resolve(["customerName"]), [], info with { AllColumns = false });
        Assert.Equal(1, PdfReader.Open(new MemoryStream(empty.ToArray()), PdfDocumentOpenMode.Import).PageCount);
    }

    [Fact]
    public void Pdf_fonts_fall_back_to_the_font_embedded_in_pdfsharp()
    {
        PdfFonts.EnsureRegistered();
        Assert.False(string.IsNullOrEmpty(PdfFonts.Source));
        var (regular, bold) = PdfFonts.Embedded();
        foreach (var font in new[] { regular, bold })
            Assert.True(font.Length > 10_000 && (font[0], font[1], font[2], font[3]) == (0, 1, 0, 0), "a TrueType font");
    }
}
