using System.Text;
using System.Text.RegularExpressions;

namespace Fitzobit.Domain.Common;

/// <summary>
/// Canonical forms used for matching and de-duplication. Provider-independent, pure functions.
/// </summary>
public static partial class Normalization
{
    /// <summary>
    /// Reduces an Indian mobile number to its 10 significant digits
    /// ("+91 98765 43210", "09876543210" and "9876543210" all become "9876543210").
    /// Returns the bare digits unchanged when they do not look like an Indian mobile number.
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 14 && digits.StartsWith("0091")) digits = digits[4..];
        else if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];

        return digits.Length == 0 ? null : digits;
    }

    /// <summary>Lower-cases, trims and collapses whitespace so "RS Puram ,  Coimbatore" == "rs puram, coimbatore".</summary>
    public static string? NormalizeLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;

        var collapsed = Whitespace().Replace(location.Trim(), " ");
        collapsed = SpaceBeforeComma().Replace(collapsed, ",");
        return collapsed.ToLowerInvariant();
    }

    /// <summary>Stable upper-case code from a display name, e.g. "Large Fit Box - 6 Days" becomes "LARGE-FIT-BOX-6-DAYS".</summary>
    public static string ToCode(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastDash = true;
        foreach (var ch in name.ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) { sb.Append(ch); lastDash = false; }
            else if (!lastDash) { sb.Append('-'); lastDash = true; }
        }
        return sb.ToString().TrimEnd('-');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s+,")]
    private static partial Regex SpaceBeforeComma();
}
