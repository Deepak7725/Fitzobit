using System.Text.RegularExpressions;
using Fitzobit.Domain.Common;

namespace Fitzobit.Application.Customers;

public enum PhoneCheck
{
    Missing,
    ValidMobile,
    Landline,
    ScientificNotation,
    ContainsLetters,
    Invalid
}

public sealed record PhoneValidationResult(PhoneCheck Check, string? Normalized);

/// <summary>
/// Indian phone validation. Mobile numbers are 10 digits starting 6–9 (an optional +91 / 0091 / 0 prefix is removed).
/// 10-digit numbers starting 2–5 are accepted as landlines with a warning.
/// </summary>
public static partial class PhoneNumberValidator
{
    public static PhoneValidationResult Validate(string? raw)
    {
        var text = Normalization.RemoveInvisible(raw ?? "").Trim(); // hidden marks from a pasted number are ignored
        if (text.Length == 0) return new(PhoneCheck.Missing, null);

        if (ScientificNotation().IsMatch(text)) return new(PhoneCheck.ScientificNotation, null);
        if (text.Any(char.IsLetter)) return new(PhoneCheck.ContainsLetters, null);
        if (!AllowedCharacters().IsMatch(text)) return new(PhoneCheck.Invalid, null);

        var normalized = Normalization.NormalizePhone(text);
        if (normalized is null || normalized.Length != 10) return new(PhoneCheck.Invalid, normalized);

        return normalized[0] switch
        {
            >= '6' and <= '9' => new(PhoneCheck.ValidMobile, normalized),
            >= '2' and <= '5' => new(PhoneCheck.Landline, normalized),
            _ => new(PhoneCheck.Invalid, normalized)
        };
    }

    [GeneratedRegex(@"^\d(\.\d+)?E\+?\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex ScientificNotation();

    /// <summary>Digits plus common separators only: spaces, +, -, ., (, ).</summary>
    [GeneratedRegex(@"^[\d\s+\-.()]+$")]
    private static partial Regex AllowedCharacters();
}
