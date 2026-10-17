using System.Globalization;
using System.Net;
using System.Text.Json;
using Fitzobit.Application.Geocoding;
using Fitzobit.Domain.Geography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Geocoding;

/// <summary>Spaces requests to a provider (e.g. the public Nominatim allows one per second), across all callers.</summary>
public sealed class RequestGate(int minIntervalMs)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _last = DateTime.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var wait = _last.AddMilliseconds(minIntervalMs) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _last = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>Used when Geocoding:Provider is "None": every lookup reports NotConfigured, nothing leaves the server.</summary>
public sealed class DisabledGeocodingService : IGeocodingService
{
    public string ProviderName => "None";
    public bool IsConfigured => false;

    public Task<GeocodeResponse> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeocodeResponse(GeocodeOutcome.NotConfigured, Message: "Geocoding is not configured."));
}

/// <summary>
/// OpenStreetMap Nominatim (https://nominatim.org): free, no API key. Follows the public usage policy: an identifying
/// User-Agent, at most one request per second (shared <see cref="RequestGate"/>), results limited to the country and,
/// when known, the service city's area.
/// </summary>
public sealed class NominatimGeocodingService(HttpClient http, NominatimRequestGate gate, IOptions<GeocodingOptions> options,
    ILogger<NominatimGeocodingService> logger) : IGeocodingService
{
    public string ProviderName => "OpenStreetMap";
    public bool IsConfigured => true;

    public async Task<GeocodeResponse> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken = default)
    {
        var o = options.Value.Nominatim;
        var p = new List<string> { "format=jsonv2", "limit=1", "addressdetails=0", "accept-language=en", $"countrycodes={Uri.EscapeDataString(query.CountryCode)}" };
        // No city= next to postalcode: Nominatim then answers with the city itself (rejected as too coarse). The viewbox
        // below already keeps the pincode inside the service city's area.
        if (query.Kind == GeocodeQueryKind.PostalCode && !string.IsNullOrWhiteSpace(query.PostalCode))
            p.Add($"postalcode={Uri.EscapeDataString(query.PostalCode)}");
        else p.Add($"q={Uri.EscapeDataString(query.Text)}");
        if (query.Kind != GeocodeQueryKind.City && query.Bounds is { } b)
            p.Add(FormattableString.Invariant($"viewbox={b.West},{b.North},{b.East},{b.South}&bounded=1"));
        if (!string.IsNullOrWhiteSpace(o.ContactEmail)) p.Add($"email={Uri.EscapeDataString(o.ContactEmail)}");

        try
        {
            await gate.WaitAsync(cancellationToken);
            using var response = await http.GetAsync("search?" + string.Join('&', p), cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return GeocodeResponse.RateLimited("the geocoding service asked to slow down");
            if (response.StatusCode is HttpStatusCode.Forbidden) return GeocodeResponse.Failed("the geocoding service refused the request");
            if (!response.IsSuccessStatusCode) return GeocodeResponse.Failed($"the geocoding service answered {(int)response.StatusCode}");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) return GeocodeResponse.NotFound();
            var hit = json.RootElement[0];
            var point = new GeoPoint(Number(hit, "lat"), Number(hit, "lon"));
            return new GeocodeResponse(GeocodeOutcome.Found, point, Precision(hit), Text(hit, "display_name"), Bounds(hit));
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or FormatException or KeyNotFoundException
                                       || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Nominatim lookup failed: {Error}", e.Message);
            return GeocodeResponse.Failed(e is TaskCanceledException ? "the geocoding service did not answer in time" : "the geocoding service could not be reached");
        }
    }

    /// <summary>Maps Nominatim's address type / place rank (30 = house … 16 = city) to a precision.</summary>
    internal static GeoPrecision Precision(JsonElement hit)
    {
        var type = Text(hit, "addresstype") ?? Text(hit, "type");
        if (type is "postcode") return GeoPrecision.PostalCode;
        if (type is "city" or "municipality" or "city_district" or "district" or "county" or "state_district") return GeoPrecision.City;
        if (type is "state" or "region" or "country") return GeoPrecision.Region;
        var rank = hit.TryGetProperty("place_rank", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
        return rank switch
        {
            >= 28 => GeoPrecision.Building,
            >= 26 => GeoPrecision.Street,
            >= 17 => GeoPrecision.Locality,
            >= 12 => GeoPrecision.City,
            > 0 => GeoPrecision.Region,
            _ => GeoPrecision.Unknown
        };
    }

    private static GeoBounds? Bounds(JsonElement hit)
    {
        if (!hit.TryGetProperty("boundingbox", out var box) || box.GetArrayLength() != 4) return null;
        double At(int i) => double.Parse(box[i].GetString()!, CultureInfo.InvariantCulture);
        return new GeoBounds(At(0), At(2), At(1), At(3)); // Nominatim order: south, north, west, east
    }

    private static double Number(JsonElement e, string name) =>
        double.Parse(e.GetProperty(name).GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Google Geocoding API. Needs Geocoding:Google:ApiKey (environment variable or user secret; never in source control).
/// Results are limited to the country and biased to the service city's area.
/// </summary>
public sealed class GoogleGeocodingService(HttpClient http, GoogleRequestGate gate, IOptions<GeocodingOptions> options,
    ILogger<GoogleGeocodingService> logger) : IGeocodingService
{
    public string ProviderName => "Google";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Google.ApiKey);

    public async Task<GeocodeResponse> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return new GeocodeResponse(GeocodeOutcome.NotConfigured, Message: "Google geocoding needs an API key.");
        var components = $"country:{query.CountryCode.ToUpperInvariant()}";
        if (query.Kind == GeocodeQueryKind.PostalCode && !string.IsNullOrWhiteSpace(query.PostalCode)) components += $"|postal_code:{query.PostalCode}";
        var p = new List<string> { $"address={Uri.EscapeDataString(query.Text)}", $"components={Uri.EscapeDataString(components)}", "language=en" };
        if (query.Kind != GeocodeQueryKind.City && query.Bounds is { } b)
            p.Add("bounds=" + Uri.EscapeDataString(FormattableString.Invariant($"{b.South},{b.West}|{b.North},{b.East}")));
        p.Add($"key={Uri.EscapeDataString(options.Value.Google.ApiKey!)}");

        try
        {
            await gate.WaitAsync(cancellationToken);
            using var response = await http.GetAsync("?" + string.Join('&', p), cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return GeocodeResponse.RateLimited("the geocoding service asked to slow down");
            if (!response.IsSuccessStatusCode) return GeocodeResponse.Failed($"the geocoding service answered {(int)response.StatusCode}");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var status = json.RootElement.GetProperty("status").GetString();
            switch (status)
            {
                case "OK": break;
                case "ZERO_RESULTS": return GeocodeResponse.NotFound();
                case "OVER_QUERY_LIMIT": return GeocodeResponse.RateLimited("the Google quota was exceeded");
                default:
                    // REQUEST_DENIED / INVALID_REQUEST / OVER_DAILY_LIMIT / UNKNOWN_ERROR: never echo the request (it contains the key).
                    logger.LogWarning("Google geocoding returned {Status}", status);
                    return GeocodeResponse.Failed($"Google geocoding returned {status}");
            }

            var hit = json.RootElement.GetProperty("results")[0];
            var geometry = hit.GetProperty("geometry");
            var location = geometry.GetProperty("location");
            var point = new GeoPoint(location.GetProperty("lat").GetDouble(), location.GetProperty("lng").GetDouble());
            var types = hit.GetProperty("types").EnumerateArray().Select(t => t.GetString() ?? "").ToList();
            var area = geometry.TryGetProperty("bounds", out var bounds) ? bounds : geometry.GetProperty("viewport");
            var box = new GeoBounds(area.GetProperty("southwest").GetProperty("lat").GetDouble(), area.GetProperty("southwest").GetProperty("lng").GetDouble(),
                area.GetProperty("northeast").GetProperty("lat").GetDouble(), area.GetProperty("northeast").GetProperty("lng").GetDouble());
            return new GeocodeResponse(GeocodeOutcome.Found, point, Precision(types, geometry.GetProperty("location_type").GetString()),
                hit.GetProperty("formatted_address").GetString(), box);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                       || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Google geocoding failed: {Error}", e.GetType().Name);
            return GeocodeResponse.Failed(e is TaskCanceledException ? "the geocoding service did not answer in time" : "the geocoding service could not be reached");
        }
    }

    internal static GeoPrecision Precision(IReadOnlyCollection<string> types, string? locationType)
    {
        if (types.Any(t => t is "street_address" or "premise" or "subpremise" or "establishment" or "point_of_interest")) return GeoPrecision.Building;
        if (types.Contains("route")) return GeoPrecision.Street;
        if (types.Contains("postal_code")) return GeoPrecision.PostalCode;
        if (types.Any(t => t.StartsWith("sublocality") || t is "neighborhood")) return GeoPrecision.Locality;
        if (types.Contains("locality")) return GeoPrecision.City;
        if (types.Any(t => t.StartsWith("administrative_area") || t is "country")) return GeoPrecision.Region;
        return locationType switch { "ROOFTOP" => GeoPrecision.Building, "RANGE_INTERPOLATED" => GeoPrecision.Street, _ => GeoPrecision.Unknown };
    }
}

/// <summary>One gate per provider type, registered as a singleton (typed HttpClient services are transient).</summary>
public sealed class NominatimRequestGate(IOptions<GeocodingOptions> options) : RequestGateBase(options.Value.Nominatim.MinIntervalMs);

public sealed class GoogleRequestGate(IOptions<GeocodingOptions> options) : RequestGateBase(options.Value.Google.MinIntervalMs);

public abstract class RequestGateBase(int minIntervalMs)
{
    private readonly RequestGate _gate = new(minIntervalMs);
    public Task WaitAsync(CancellationToken ct) => _gate.WaitAsync(ct);
}
