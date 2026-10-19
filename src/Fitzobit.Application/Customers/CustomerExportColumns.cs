using Fitzobit.Application.Common;
using Fitzobit.Domain.Customers;

namespace Fitzobit.Application.Customers;

/// <summary>How a column's values are written: text stays text (IDs, phones, pincodes), dates become real dates.</summary>
public enum ExportColumnKind
{
    Text,
    Integer,
    Coordinate,
    Date,
    DateTime,
    Time
}

/// <param name="Key">The View Data column key (the same key the Customers table uses).</param>
/// <param name="Wraps">Whether long values may wrap onto several lines in a PDF (names, addresses); other values stay on one line.</param>
public sealed record CustomerExportColumn(string Key, string Header, ExportColumnKind Kind, double ExcelWidth, bool Wraps, Func<CustomerExportRow, object?> Value);

/// <summary>
/// Every column a customer export can contain, in the Customers table's order. Exports contain exactly the columns the
/// user asks for (normally the visible table columns), in the requested order.
/// </summary>
public static class CustomerExportColumns
{
    public static readonly IReadOnlyList<CustomerExportColumn> All =
    [
        new("customerId", "Customer ID", ExportColumnKind.Text, 15, false, r => r.CustomerId),
        new("customerName", "Customer Name", ExportColumnKind.Text, 28, true, r => r.CustomerName),
        new("phone", "Phone Number", ExportColumnKind.Text, 16, false, r => r.PhoneNumber),
        new("alternatePhone", "Alternative Phone", ExportColumnKind.Text, 18, false, r => r.AlternatePhoneNumber),
        new("address", "Address", ExportColumnKind.Text, 52, true, r => r.Address),
        new("pincode", "Pincode", ExportColumnKind.Text, 10, false, r => r.Pincode),
        new("deliveryTime", "Delivery Time", ExportColumnKind.Time, 13, false, r => r.PreferredDeliveryTime),
        new("packages", "Packages", ExportColumnKind.Text, 60, true, r => r.Packages),
        new("notes", "Notes", ExportColumnKind.Text, 60, true, r => r.Notes),
        new("startDate", "Start Date", ExportColumnKind.Date, 13, false, r => r.StartDate),
        new("endDate", "End Date", ExportColumnKind.Date, 13, false, r => r.EndDate),
        new("group", "Group Number", ExportColumnKind.Integer, 14, false, r => r.GroupNumber),
        new("status", "Status", ExportColumnKind.Text, 11, true, r => r.Status),
        new("location", "Location", ExportColumnKind.Text, 22, true, r => LocationLabel(r.GeocodeStatus, r.GeocodeMatchLevel)),
        new("latitude", "Latitude", ExportColumnKind.Coordinate, 12, false, r => r.Latitude),
        new("longitude", "Longitude", ExportColumnKind.Coordinate, 12, false, r => r.Longitude),
        new("createdAt", "Created At", ExportColumnKind.DateTime, 18, true, r => r.CreatedAt),
        new("updatedAt", "Updated At", ExportColumnKind.DateTime, 18, true, r => r.UpdatedAt),
    ];

    /// <summary>
    /// The columns for <paramref name="keys"/>, in that order. Null or empty means every column (a full export).
    /// Unknown or repeated keys are rejected, so a file never silently differs from what the user chose.
    /// </summary>
    public static IReadOnlyList<CustomerExportColumn> Resolve(IReadOnlyList<string>? keys)
    {
        if (keys is not { Count: > 0 }) return All;
        var columns = new List<CustomerExportColumn>(keys.Count);
        foreach (var key in keys)
        {
            var column = All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
                         ?? throw new BusinessRuleException($"Unknown export column \"{Truncate(key)}\".");
            if (columns.Contains(column)) throw new BusinessRuleException($"The column \"{column.Header}\" was chosen twice.");
            columns.Add(column);
        }
        return columns;
    }

    /// <summary>The same wording as the location badge on the Customers screen.</summary>
    public static string LocationLabel(GeocodeStatus status, GeoMatchLevel? level) => status switch
    {
        GeocodeStatus.Located => level switch
        {
            GeoMatchLevel.Pincode => "Approximate (pincode)",
            GeoMatchLevel.Locality => "Located (area)",
            _ => "Located"
        },
        GeocodeStatus.Pending => "Looking up",
        _ => "Location unavailable"
    };

    private static string Truncate(string? value) => value is { Length: > 40 } ? value[..40] + "…" : value ?? "";
}
