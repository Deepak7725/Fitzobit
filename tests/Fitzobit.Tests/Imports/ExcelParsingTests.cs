using System.IO.Compression;
using Fitzobit.Application.Imports;
using Fitzobit.Infrastructure.Imports;
using Microsoft.Extensions.Options;

namespace Fitzobit.Tests.Imports;

public class ExcelParsingTests
{
    private static readonly ExcelCustomerFileReader Reader = new(Options.Create(new ImportOptions()));

    [Theory]
    [InlineData("CustomerID", "CustomerID")]
    [InlineData(" customerid ", "CustomerID")]
    [InlineData("Customer ID *", "CustomerID")]
    [InlineData("CUSTOMER_ID", "CustomerID")]
    [InlineData("Customer Name", "CustomerName")]
    [InlineData("Phone Number (Required)", "PhoneNumber")]
    [InlineData("Mobile", "PhoneNumber")]
    [InlineData("Location (Address)", "Location")]
    [InlineData("plan", "Plan")]
    [InlineData("Notes", null)]
    [InlineData("", null)]
    public void Headers_are_normalised(string header, string? expected) =>
        Assert.Equal(expected, CustomerFileHeaders.Canonical(header));

    [Fact]
    public void Reads_rows_and_keeps_numeric_phone_numbers_as_full_digits()
    {
        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders,
            ["CUST001", "Divya S", 9876543210d, "Anna Nagar, Chennai - 600040", "Regular Fit Box - 6 Days"],
            [null, null, null, null, null],
            ["CUST002", "Arjun K", "+91 91234 56789", "RS Puram, Coimbatore - 641002", "Large Fit Box - 6 Days"]);

        var result = Reader.Read(file, "customers.xlsx");

        Assert.True(result.Success);
        Assert.Equal("Customers", result.SheetName);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1, result.EmptyRowsSkipped);
        Assert.Equal("9876543210", result.Rows[0].PhoneNumber);
        Assert.Equal(2, result.Rows[0].RowNumber);
        Assert.Equal(4, result.Rows[1].RowNumber);
        Assert.Equal("+91 91234 56789", result.Rows[1].PhoneNumber);
    }

    [Fact]
    public void Tolerates_header_case_spacing_order_and_extra_columns()
    {
        using var file = TestWorkbook.Create("Sheet1", 3, ["  plan ", "Notes", "customer id", "PHONE NUMBER", "Customer Name", "location"],
            ["Large Fit Box - 6 Days", "call first", "C-9", "9000011111", "Test Person", "Thanjavur 613001"]);

        var result = Reader.Read(file, "x.xlsx");

        Assert.True(result.Success);
        var row = Assert.Single(result.Rows);
        Assert.Equal(("C-9", "Test Person", "Large Fit Box - 6 Days"), (row.CustomerId, row.CustomerName, row.Plan));
        Assert.Contains(result.Notices, n => n.Contains("Notes"));
        Assert.Contains(result.Notices, n => n.Contains("row 3"));
    }

    [Fact]
    public void Missing_required_headers_are_reported()
    {
        using var file = TestWorkbook.Create(["CustomerID", "CustomerName", "PhoneNumber", "Notes"], ["C1", "A B", "9000000001", "x"]);
        var result = Reader.Read(file, "x.xlsx");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Location") && e.Contains("Plan"));
    }

    [Fact]
    public void Duplicate_headers_are_reported()
    {
        using var file = TestWorkbook.Create(["CustomerID", "CustomerName", "PhoneNumber", "Location", "Plan", "Mobile"],
            ["C1", "A B", "9000000001", "Chennai 600001", "x", "9000000002"]);
        var result = Reader.Read(file, "x.xlsx");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate column PhoneNumber"));
    }

    [Fact]
    public void Formulas_are_not_evaluated_and_are_flagged()
    {
        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders,
            ["C1", new TestWorkbook.Formula("\"Formula \"&\"Name\""), "9000000001", "Chennai 600001", "Large Fit Box - 6 Days"]);
        var result = Reader.Read(file, "x.xlsx");
        Assert.True(result.Success);
        Assert.Contains(result.Rows[0].CellNotes, n => n.Contains("formula"));
    }

    [Fact]
    public void Non_excel_content_is_rejected()
    {
        using var file = new MemoryStream("CustomerID,CustomerName\nC1,Test"u8.ToArray());
        var result = Reader.Read(file, "customers.xlsx");
        Assert.False(result.Success);
        Assert.Contains("not a valid Excel workbook", result.Errors[0]);
    }

    [Fact]
    public void Macro_content_is_rejected_before_parsing()
    {
        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders, ["C1", "A B", "9000000001", "Chennai 600001", "x"]);
        using var withMacro = new MemoryStream();
        file.CopyTo(withMacro);
        using (var zip = new ZipArchive(withMacro, ZipArchiveMode.Update, leaveOpen: true))
        using (var entry = zip.CreateEntry("xl/vbaProject.bin").Open())
            entry.Write([1, 2, 3]);
        withMacro.Position = 0;

        var result = Reader.Read(withMacro, "customers.xlsx");

        Assert.False(result.Success);
        Assert.Contains("macros", result.Errors[0]);
    }
}
