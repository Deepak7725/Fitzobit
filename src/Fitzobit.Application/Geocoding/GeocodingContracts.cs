using Fitzobit.Domain.Geography;

namespace Fitzobit.Application.Geocoding;

/// <summary>
/// A geocoding provider (OpenStreetMap Nominatim, Google, …). Implementations live in Infrastructure; business rules
/// (which queries to try, what precision is acceptable, the service area) live in <see cref="CustomerGeocodingService"/>.
/// </summary>
public interface IGeocodingService
{
    /// <summary>Short name stored with the coordinates, e.g. "OpenStreetMap".</summary>
    string ProviderName { get; }

    /// <summary>False when no provider is configured (lookups then return <see cref="GeocodeOutcome.NotConfigured"/>).</summary>
    bool IsConfigured { get; }

    /// <summary>One lookup. Never throws for provider errors: they are reported as <see cref="GeocodeOutcome.Failed"/> / <see cref="GeocodeOutcome.RateLimited"/>.</summary>
    Task<GeocodeResponse> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken = default);
}

public enum GeocodeQueryKind
{
    /// <summary>A free-text address ("12 Main Road, RS Puram, Coimbatore 641002").</summary>
    Address,
    /// <summary>A postal code (pincode) in the service city.</summary>
    PostalCode,
    /// <summary>The service city itself (to learn its area).</summary>
    City
}

/// <param name="Text">Free text for <see cref="GeocodeQueryKind.Address"/>/<see cref="GeocodeQueryKind.City"/>.</param>
/// <param name="Bounds">Preferred search area (the service city); providers restrict or bias results to it.</param>
public sealed record GeocodeQuery(
    GeocodeQueryKind Kind,
    string Text,
    string? PostalCode,
    string? City,
    string? State,
    string CountryCode,
    GeoBounds? Bounds);

public enum GeocodeOutcome
{
    Found,
    NotFound,
    /// <summary>The provider asked us to slow down; try again later.</summary>
    RateLimited,
    /// <summary>Network/provider error; try again later.</summary>
    Failed,
    NotConfigured
}

/// <summary>What the provider matched, from most to least precise.</summary>
public enum GeoPrecision
{
    Building,
    Street,
    Locality,
    PostalCode,
    City,
    Region,
    Unknown
}

public sealed record GeocodeResponse(
    GeocodeOutcome Outcome,
    GeoPoint? Point = null,
    GeoPrecision Precision = GeoPrecision.Unknown,
    string? MatchedPlace = null,
    GeoBounds? Bounds = null,
    string? Message = null)
{
    public static GeocodeResponse NotFound(string? message = null) => new(GeocodeOutcome.NotFound, Message: message);
    public static GeocodeResponse Failed(string message) => new(GeocodeOutcome.Failed, Message: message);
    public static GeocodeResponse RateLimited(string message) => new(GeocodeOutcome.RateLimited, Message: message);
}

/// <summary>A latitude/longitude rectangle.</summary>
public sealed record GeoBounds(double South, double West, double North, double East)
{
    public GeoPoint Center => new((South + North) / 2, (West + East) / 2);

    /// <summary>True when the point lies inside the rectangle grown by <paramref name="marginKm"/> on every side.</summary>
    public bool Contains(GeoPoint p, double marginKm = 0)
    {
        var dLat = marginKm / 110.574;
        var dLng = marginKm / (111.320 * Math.Max(0.1, Math.Cos(Center.Latitude * Math.PI / 180)));
        return p.Latitude >= South - dLat && p.Latitude <= North + dLat && p.Longitude >= West - dLng && p.Longitude <= East + dLng;
    }

    public string Serialize() => FormattableString.Invariant($"{South},{West},{North},{East}");

    public static GeoBounds? Parse(string? value)
    {
        var parts = value?.Split(',');
        if (parts is not { Length: 4 }) return null;
        var n = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n[i])) return null;
        return new GeoBounds(n[0], n[1], n[2], n[3]);
    }
}

/// <summary>Wakes the background geocoder when customers need a location (instead of waiting for its next poll).</summary>
public interface IGeocodingQueue
{
    void Notify();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class GeocodingQueue : IGeocodingQueue
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>Configuration section "Geocoding". Secrets (the Google API key) come from environment variables or user secrets.</summary>
public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>Nominatim (OpenStreetMap, free, no key) | Google (needs ApiKey) | None.</summary>
    public string Provider { get; set; } = "Nominatim";

    /// <summary>ISO country code that every lookup is limited to.</summary>
    public string CountryCode { get; set; } = "in";

    public string Country { get; set; } = "India";

    /// <summary>Lookups that fail with an error are retried this many times (with growing delays), then marked Failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Matches further than this outside the service city's area are rejected as wrong.</summary>
    public double ServiceAreaMarginKm { get; set; } = 15;

    /// <summary>When the street address cannot be found, use the pincode area (marked approximate).</summary>
    public bool AllowPincodeApproximation { get; set; } = true;

    /// <summary>Customers handled per background batch.</summary>
    public int BatchSize { get; set; } = 25;

    public NominatimOptions Nominatim { get; set; } = new();
    public GoogleGeocodingOptions Google { get; set; } = new();
}

public sealed class NominatimOptions
{
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>Identifies the app, as the Nominatim usage policy requires.</summary>
    public string UserAgent { get; set; } = "Fitzobit-Delivery-Management/1.0";

    /// <summary>Optional contact address sent with requests (Nominatim policy recommends it for regular use).</summary>
    public string? ContactEmail { get; set; }

    /// <summary>The public Nominatim service allows at most one request per second.</summary>
    public int MinIntervalMs { get; set; } = 1100;

    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class GoogleGeocodingOptions
{
    public string BaseUrl { get; set; } = "https://maps.googleapis.com/maps/api/geocode/json";

    /// <summary>Secret: set Geocoding__Google__ApiKey as an environment variable or user secret, never in appsettings.json.</summary>
    public string? ApiKey { get; set; }

    public int MinIntervalMs { get; set; } = 50;
    public int TimeoutSeconds { get; set; } = 10;
}
