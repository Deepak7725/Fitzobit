using System.Text.RegularExpressions;

namespace Fitzobit.Application.Imports;

public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxRows { get; set; } = 20_000;

    /// <summary>Guard against compressed "zip bomb" workbooks.</summary>
    public long MaxUncompressedBytes { get; set; } = 250L * 1024 * 1024;
}

/// <summary>One data row as read from the file. Values are the cell text exactly as Excel stored it.</summary>
public sealed record RawCustomerRow(
    int RowNumber,
    string? CustomerId,
    string? CustomerName,
    string? PhoneNumber,
    string? Location,
    string? Plan,
    IReadOnlyList<string> CellNotes);

public sealed record CustomerFileReadResult(
    bool Success,
    string? SheetName,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Notices,
    IReadOnlyList<RawCustomerRow> Rows,
    int EmptyRowsSkipped)
{
    public static CustomerFileReadResult Fail(params string[] errors) => new(false, null, errors, [], [], 0);
}

/// <summary>Reads an uploaded customer workbook. Treats the file as untrusted: no macros, formulas or links are executed.</summary>
public interface ICustomerFileReader
{
    CustomerFileReadResult Read(Stream content, string fileName);
}

public sealed record TemplatePlan(string Name, string? Category, string? Variants);

public interface ICustomerTemplateGenerator
{
    byte[] Generate(IReadOnlyList<TemplatePlan> plans);
}

/// <summary>The required columns and the header spellings accepted for each.</summary>
public static partial class CustomerFileHeaders
{
    public const string CustomerId = "CustomerID";
    public const string CustomerName = "CustomerName";
    public const string PhoneNumber = "PhoneNumber";
    public const string Location = "Location";
    public const string Plan = "Plan";

    public static readonly IReadOnlyList<string> Required = [CustomerId, CustomerName, PhoneNumber, Location, Plan];

    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        ["customerid"] = CustomerId, ["custid"] = CustomerId, ["customercode"] = CustomerId, ["customerno"] = CustomerId,
        ["customername"] = CustomerName, ["name"] = CustomerName, ["fullname"] = CustomerName,
        ["phonenumber"] = PhoneNumber, ["phone"] = PhoneNumber, ["phoneno"] = PhoneNumber, ["mobile"] = PhoneNumber,
        ["mobilenumber"] = PhoneNumber, ["mobileno"] = PhoneNumber, ["contactnumber"] = PhoneNumber, ["whatsappnumber"] = PhoneNumber,
        ["location"] = Location, ["locationaddress"] = Location, ["address"] = Location, ["deliveryaddress"] = Location,
        ["plan"] = Plan, ["planname"] = Plan, ["fitzobitplan"] = Plan, ["subscriptionplan"] = Plan,
    };

    /// <summary>
    /// Maps a header cell to its canonical column, ignoring case, spaces, punctuation and required-markers
    /// ("Customer ID *", " customerid ", "Phone_Number (required)"). Returns null for unrelated headers.
    /// </summary>
    public static string? Canonical(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var text = RequiredMarker().Replace(header.ToLowerInvariant(), "");
        var compact = NonAlphanumeric().Replace(text, "");
        return Accepted.GetValueOrDefault(compact);
    }

    [GeneratedRegex(@"\(\s*(required|mandatory)\s*\)|\*")]
    private static partial Regex RequiredMarker();

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
