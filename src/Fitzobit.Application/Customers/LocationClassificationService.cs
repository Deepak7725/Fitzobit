using System.Text.RegularExpressions;
using Fitzobit.Domain.Common;

namespace Fitzobit.Application.Customers;

public enum PincodeStatus
{
    NotDetected,
    Detected,
    Multiple
}

public sealed record LocationClassification(
    string? OriginalLocation,
    string? NormalizedLocation,
    string? Pincode,
    PincodeStatus PincodeStatus,
    IReadOnlyList<string> PincodeCandidates,
    string? Locality,
    string? City);

public interface ILocationClassificationService
{
    /// <summary>
    /// Classifies a free-text Indian address. The original text is returned untouched; a pincode is only reported
    /// when exactly one six-digit candidate is found (or one is explicitly labelled "PIN").
    /// </summary>
    LocationClassification Classify(string? location);
}

public sealed class LocationClassificationOptions
{
    public const string SectionName = "LocationClassification";

    /// <summary>City names recognised in addresses (used to separate city from locality).</summary>
    public List<string> KnownCities { get; set; } = [];

    /// <summary>Alternative spellings mapped to the canonical city name, e.g. "Bangalore" -> "Bengaluru".</summary>
    public Dictionary<string, string> CityAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Region words removed before locality/city detection (states, "India").</summary>
    public List<string> RegionWords { get; set; } = [];
}

public sealed partial class LocationClassificationService(LocationClassificationOptions options) : ILocationClassificationService
{
    private readonly Dictionary<string, string> _cities = BuildCityLookup(options);
    private readonly HashSet<string> _regionWords = new(options.RegionWords.Select(r => r.Trim()), StringComparer.OrdinalIgnoreCase);

    public LocationClassification Classify(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return new(location, null, null, PincodeStatus.NotDetected, [], null, null);

        var (pincode, status, candidates) = DetectPincode(location);

        // Strip pincodes and "PIN" labels before deriving the normalized text, locality and city.
        var withoutPin = PincodePattern().Replace(location, " ");
        withoutPin = PinLabel().Replace(withoutPin, " ");

        var parts = Separators().Split(withoutPin)
            .Select(s => Whitespace().Replace(s, " ").Trim(' ', '.', '#', ':'))
            .Where(s => s.Length > 0 && !_regionWords.Contains(s))
            .ToList();

        // The normalized location keeps door numbers so different addresses stay distinct;
        // locality/city detection works on the parts with house/door/flat numbers removed.
        var normalized = parts.Count == 0 ? null : Normalization.NormalizeLocation(string.Join(", ", parts));
        var areaParts = parts
            .Select(p => HouseNumber().Replace(p, "").Trim(' ', ',', '.', '-'))
            .Where(p => p.Any(char.IsLetter))
            .ToList();
        var (locality, city) = SplitLocalityAndCity(areaParts);

        return new(location, normalized, pincode, status, candidates, locality, city);
    }

    private static (string? Pincode, PincodeStatus Status, IReadOnlyList<string> Candidates) DetectPincode(string location)
    {
        var matches = PincodePattern().Matches(location);
        var candidates = matches.Select(m => m.Groups[1].Value + m.Groups[2].Value).Distinct().ToList();

        if (candidates.Count == 0) return (null, PincodeStatus.NotDetected, candidates);
        if (candidates.Count == 1) return (candidates[0], PincodeStatus.Detected, candidates);

        // Several six-digit numbers: only trust one that is explicitly labelled "PIN"/"Pincode".
        var labelled = LabelledPincode().Matches(location)
            .Select(m => m.Groups[1].Value + m.Groups[2].Value).Distinct().ToList();
        return labelled.Count == 1
            ? (labelled[0], PincodeStatus.Detected, candidates)
            : (null, PincodeStatus.Multiple, candidates);
    }

    private (string? Locality, string? City) SplitLocalityAndCity(List<string> segments)
    {
        if (segments.Count == 0) return (null, null);

        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var city = MatchCity(segments[i]);
            if (city is null) continue;

            var locality = i > 0 ? segments[i - 1] : segments.Count > 1 ? segments[1] : null;
            if (locality is not null && MatchCity(locality) is not null) locality = null;
            return (locality, city);
        }

        // No recognised city: with several segments the last is taken as the town, the one before as the locality.
        return segments.Count >= 2 ? (segments[^2], segments[^1]) : (segments[0], null);
    }

    private string? MatchCity(string segment)
    {
        if (_cities.TryGetValue(segment, out var exact)) return exact;
        foreach (var word in Whitespace().Split(segment))
            if (_cities.TryGetValue(word.Trim('.', ','), out var city)) return city;
        return null;
    }

    private static Dictionary<string, string> BuildCityLookup(LocationClassificationOptions options)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var city in options.KnownCities.Where(c => !string.IsNullOrWhiteSpace(c)))
            lookup[city.Trim()] = city.Trim();
        foreach (var (alias, canonical) in options.CityAliases)
            lookup[alias.Trim()] = canonical.Trim();
        return lookup;
    }

    /// <summary>Six digits, first non-zero, optionally written "641 002", not part of a longer number.</summary>
    [GeneratedRegex(@"(?<![\d])([1-9]\d{2})\s?(\d{3})(?![\d])")]
    private static partial Regex PincodePattern();

    [GeneratedRegex(@"\bpin\s*(?:code)?\s*(?:no\.?)?\s*[:\-]?\s*([1-9]\d{2})\s?(\d{3})(?![\d])", RegexOptions.IgnoreCase)]
    private static partial Regex LabelledPincode();

    [GeneratedRegex(@"\bpin\s*(?:code)?\s*(?:no\.?)?\s*[:\-]?", RegexOptions.IgnoreCase)]
    private static partial Regex PinLabel();

    /// <summary>A leading house/door/flat number: "12 ", "No. 12, ", "#4 ", "Door 4", "Flat 2B", "12/3A ".</summary>
    [GeneratedRegex(@"^(?:(?:door|flat|plot|house|shop|h)\.?\s*(?:no\.?)?\s*:?\s*|no\.?\s*:?\s*)?#?\d+[a-z]?(?:\s*[/-]\s*\d+[a-z]?)*\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex HouseNumber();

    [GeneratedRegex(@"[,\n\r|;]|\s[-–—]\s|\s[-–—]|[-–—]\s")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
