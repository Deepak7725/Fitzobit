using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Geography;
using Fitzobit.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.Geocoding;

/// <param name="City">The one city Fitzobit delivers in; null until an administrator sets it.</param>
/// <param name="Bounds">The city's area as found by the geocoder; addresses are searched in it and matches far outside it are rejected.</param>
public sealed record ServiceArea(string? City, string? State, string Country, string CountryCode, GeoBounds? Bounds)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(City);
}

/// <param name="AreaFound">Whether the city's area is known (searches are then limited to it).</param>
public sealed record ServiceAreaDto(
    string? City,
    string? State,
    string Country,
    bool IsConfigured,
    bool AreaFound,
    double? CenterLatitude,
    double? CenterLongitude,
    string? AreaMessage,
    string GeocodingProvider,
    bool GeocodingConfigured,
    DateTime? UpdatedAt,
    string? UpdatedBy);

public sealed class SaveServiceAreaRequest
{
    public string City { get; set; } = "";
    public string? State { get; set; }
}

/// <summary>The delivery service area (Settings → Delivery area). Stored in AppSettings; never assumed.</summary>
public sealed class ServiceAreaService(IFitzobitDbContext db, IGeocodingService geocoder, IGeocodingQueue queue, IAuditLogger audit,
    ICurrentUser currentUser, IOptions<GeocodingOptions> options)
{
    public const string CityKey = "Delivery.ServiceCity";
    public const string StateKey = "Delivery.ServiceState";
    public const string BoundsKey = "Delivery.AreaBounds";
    public const string AreaMessageKey = "Delivery.AreaMessage";

    public async Task<ServiceArea> GetAreaAsync(CancellationToken ct = default)
    {
        var values = await ValuesAsync(ct);
        return new ServiceArea(values.GetValueOrDefault(CityKey)?.Value, values.GetValueOrDefault(StateKey)?.Value, options.Value.Country,
            options.Value.CountryCode, GeoBounds.Parse(values.GetValueOrDefault(BoundsKey)?.Value));
    }

    public async Task<ServiceAreaDto> GetAsync(CancellationToken ct = default)
    {
        var values = await ValuesAsync(ct);
        var area = new ServiceArea(values.GetValueOrDefault(CityKey)?.Value, values.GetValueOrDefault(StateKey)?.Value, options.Value.Country,
            options.Value.CountryCode, GeoBounds.Parse(values.GetValueOrDefault(BoundsKey)?.Value));
        var city = values.GetValueOrDefault(CityKey);
        return new ServiceAreaDto(area.City, area.State, area.Country, area.IsConfigured, area.Bounds is not null, area.Bounds?.Center.Latitude,
            area.Bounds?.Center.Longitude, values.GetValueOrDefault(AreaMessageKey)?.Value, geocoder.ProviderName, geocoder.IsConfigured,
            city?.UpdatedAt, city?.UpdatedBy);
    }

    /// <summary>Saves the city and looks up its area once, so customer addresses can be searched within it.</summary>
    public async Task<ServiceAreaDto> SaveAsync(SaveServiceAreaRequest request, CancellationToken ct = default)
    {
        var city = Clean(request.City) ?? throw new BusinessRuleException("Enter the city Fitzobit delivers in.");
        var state = Clean(request.State);
        if (city.Length > 100 || state?.Length > 100) throw new BusinessRuleException("City and state must be at most 100 characters.");

        var previous = await GetAreaAsync(ct);
        GeoBounds? bounds = null;
        string message;
        var lookup = await geocoder.GeocodeAsync(new GeocodeQuery(GeocodeQueryKind.City, string.Join(", ", new[] { city, state, options.Value.Country }
            .Where(x => !string.IsNullOrWhiteSpace(x))), null, city, state, options.Value.CountryCode, null), ct);
        switch (lookup.Outcome)
        {
            case GeocodeOutcome.Found when lookup.Bounds is not null:
                bounds = lookup.Bounds;
                message = $"Found: {Truncate(lookup.MatchedPlace ?? city, 200)}";
                break;
            case GeocodeOutcome.Found or GeocodeOutcome.NotFound:
                message = "The city's area was not found; addresses are searched across the country.";
                break;
            case GeocodeOutcome.NotConfigured:
                message = "Geocoding is not configured, so the city's area could not be looked up.";
                break;
            default:
                message = $"The city's area could not be looked up right now ({lookup.Message}). Save again later.";
                break;
        }

        await SetAsync(CityKey, city, ct);
        await SetAsync(StateKey, state, ct);
        await SetAsync(BoundsKey, bounds?.Serialize(), ct);
        await SetAsync(AreaMessageKey, message, ct);
        audit.RecordChange(AuditActions.SettingsUpdated, "Settings", "DeliveryArea", $"Delivery area set to {city}",
            new Dictionary<string, object?> { ["City"] = previous.City, ["State"] = previous.State },
            new Dictionary<string, object?> { ["City"] = city, ["State"] = state, ["AreaFound"] = bounds is not null });
        await db.SaveChangesAsync(ct);
        queue.Notify(); // customers waiting for a service city can be located now
        return await GetAsync(ct);
    }

    private async Task<Dictionary<string, AppSetting>> ValuesAsync(CancellationToken ct) =>
        await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith("Delivery.")).ToDictionaryAsync(s => s.Key, ct);

    private async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null) db.AppSettings.Add(setting = new AppSetting { Key = key });
        setting.Value = value;
        setting.UpdatedBy = currentUser.UserName;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
