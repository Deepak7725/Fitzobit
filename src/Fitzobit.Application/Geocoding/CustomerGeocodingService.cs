using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Geography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.Geocoding;

/// <param name="Skipped">Why nothing was attempted (not configured, no service city), when that is the case.</param>
public sealed record GeocodeBatchResult(int Processed, int Located, int NotFound, int Retrying, int Failed, string? Skipped = null);

/// <summary>
/// Finds customers' coordinates from their address, always within the configured service city:
/// <list type="number">
/// <item>the full address (with pincode and city),</item>
/// <item>the locality recognised in the address,</item>
/// <item>the pincode area (only if allowed; marked approximate).</item>
/// </list>
/// A match that is only the city (or region), or that lies outside the service area, is rejected: coordinates are never
/// guessed. Provider errors are retried later with growing delays; saving a customer never waits for geocoding.
/// </summary>
public sealed class CustomerGeocodingService(
    IFitzobitDbContext db,
    IGeocodingService geocoder,
    ServiceAreaService serviceArea,
    ILocationClassificationService locations,
    IGeocodingQueue queue,
    IAuditLogger audit,
    IClock clock,
    IOptions<GeocodingOptions> options,
    ILogger<CustomerGeocodingService> logger)
{
    public const int MaxMessageLength = 300;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    private enum Attempt { Located, NotFound, Retry, Failed, NotConfigured, RateLimited }

    /// <summary>Locates customers whose lookup is due (used by the background worker).</summary>
    public async Task<GeocodeBatchResult> ProcessDueAsync(int maxCustomers, CancellationToken ct = default)
    {
        if (!geocoder.IsConfigured) return new GeocodeBatchResult(0, 0, 0, 0, 0, "Geocoding is not configured.");
        var area = await serviceArea.GetAreaAsync(ct);
        if (!area.IsConfigured) return new GeocodeBatchResult(0, 0, 0, 0, 0, "No service city is set (Settings → Delivery area).");

        var now = clock.UtcNow;
        var due = await db.Customers.AsNoTracking()
            .Where(c => c.GeocodeStatus == GeocodeStatus.Pending && (c.GeocodeNextAttemptAt == null || c.GeocodeNextAttemptAt <= now))
            .OrderBy(c => c.GeocodeNextAttemptAt ?? c.UpdatedAt).ThenBy(c => c.Id)
            .Select(c => c.Id).Take(Math.Max(1, maxCustomers)).ToListAsync(ct);

        int located = 0, notFound = 0, retrying = 0, failed = 0, processed = 0;
        foreach (var id in due)
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (customer is null || customer.GeocodeStatus != GeocodeStatus.Pending) continue;

            var result = await LocateAsync(customer, area, ct);
            if (result == Attempt.NotConfigured) break;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The customer was edited meanwhile (e.g. a new address); the next batch looks again.
                db.ChangeTracker.Clear();
                continue;
            }
            processed++;
            switch (result)
            {
                case Attempt.Located: located++; break;
                case Attempt.NotFound: notFound++; break;
                case Attempt.Failed: failed++; break;
                default: retrying++; break;
            }
            if (result == Attempt.RateLimited) break; // slow down: the rest waits for the next batch
        }
        db.ChangeTracker.Clear();
        if (processed > 0)
            logger.LogInformation("Geocoding batch: {Processed} processed, {Located} located, {NotFound} not found, {Retrying} retrying, {Failed} failed",
                processed, located, notFound, retrying, failed);
        return new GeocodeBatchResult(processed, located, notFound, retrying, failed);
    }

    /// <summary>
    /// Queues customers for a new lookup ("Retry location"). With no ids, every customer whose location is unavailable
    /// (not found, failed or waiting for a retry) is queued. Returns how many were queued.
    /// </summary>
    public async Task<int> RequestAsync(IReadOnlyCollection<int>? customerIds, CancellationToken ct = default)
    {
        var query = db.Customers.AsQueryable();
        if (customerIds is { Count: > 0 })
        {
            var ids = customerIds.Distinct().ToList();
            query = query.Where(c => ids.Contains(c.Id));
        }
        else query = query.Where(c => c.GeocodeStatus != GeocodeStatus.Located);

        var customers = await query.ToListAsync(ct);
        foreach (var c in customers) c.ResetLocation();
        if (customers.Count > 0)
        {
            audit.Record(AuditActions.LocationLookupRequested, nameof(Customer), customers.Count == 1 ? customers[0].CustomerId : null,
                customers.Count == 1 ? $"Location lookup requested for {customers[0].CustomerId}" : $"Location lookup requested for {customers.Count} customers");
            await db.SaveChangesAsync(ct);
            queue.Notify();
        }
        return customers.Count;
    }

    private async Task<Attempt> LocateAsync(Customer customer, ServiceArea area, CancellationToken ct)
    {
        var o = options.Value;
        var queries = BuildQueries(customer, area, o).ToList();
        if (queries.Count == 0) return Finish(customer, GeocodeStatus.NotFound, "No address or pincode to look up.");

        var rejected = new List<string>();
        foreach (var (query, level) in queries)
        {
            var response = await geocoder.GeocodeAsync(query, ct);
            switch (response.Outcome)
            {
                case GeocodeOutcome.NotConfigured:
                    return Attempt.NotConfigured;
                case GeocodeOutcome.RateLimited or GeocodeOutcome.Failed:
                    return ScheduleRetry(customer, response);
                case GeocodeOutcome.NotFound:
                    continue;
            }

            var point = response.Point!.Value;
            var matched = MatchLevel(level, response.Precision);
            if (matched is null)
            {
                rejected.Add($"only the {response.Precision.ToString().ToLowerInvariant()} was matched");
                continue;
            }
            if (area.Bounds is { } bounds && !bounds.Contains(point, o.ServiceAreaMarginKm))
            {
                rejected.Add($"a match outside {area.City} was ignored");
                continue;
            }

            customer.Latitude = Math.Round((decimal)point.Latitude, 6);
            customer.Longitude = Math.Round((decimal)point.Longitude, 6);
            customer.GeocodeMatchLevel = matched;
            customer.GeocodeSource = geocoder.ProviderName;
            customer.GeocodedAt = clock.UtcNow;
            customer.GeocodeAttempts++;
            customer.GeocodeNextAttemptAt = null;
            customer.GeocodeStatus = GeocodeStatus.Located;
            customer.GeocodeMessage = Truncate(response.MatchedPlace);
            return Attempt.Located;
        }

        customer.GeocodeAttempts++;
        var reason = rejected.Count > 0 ? $" ({string.Join("; ", rejected.Distinct())})" : "";
        return Finish(customer, GeocodeStatus.NotFound, $"Address not found in {area.City}{reason}. Check the address and pincode, then retry.");
    }

    private Attempt Finish(Customer customer, GeocodeStatus status, string message)
    {
        customer.Latitude = null;
        customer.Longitude = null;
        customer.GeocodeMatchLevel = null;
        customer.GeocodeStatus = status;
        customer.GeocodeNextAttemptAt = null;
        customer.GeocodedAt = clock.UtcNow;
        customer.GeocodeMessage = Truncate(message);
        customer.GeocodeSource = geocoder.ProviderName;
        return status == GeocodeStatus.NotFound ? Attempt.NotFound : Attempt.Failed;
    }

    private Attempt ScheduleRetry(Customer customer, GeocodeResponse response)
    {
        customer.GeocodeAttempts++;
        if (customer.GeocodeAttempts >= options.Value.MaxAttempts)
            return Finish(customer, GeocodeStatus.Failed, $"Location lookup failed {customer.GeocodeAttempts} times ({response.Message}). Use Retry location.");

        var delay = RetryDelays[Math.Min(customer.GeocodeAttempts - 1, RetryDelays.Length - 1)];
        customer.GeocodeNextAttemptAt = clock.UtcNow.Add(delay);
        customer.GeocodeMessage = Truncate($"Temporarily unavailable ({response.Message}); retrying automatically.");
        return response.Outcome == GeocodeOutcome.RateLimited ? Attempt.RateLimited : Attempt.Retry;
    }

    /// <summary>Address → locality → pincode area, each with the precision that is acceptable for it.</summary>
    private IEnumerable<(GeocodeQuery Query, GeoMatchLevel Level)> BuildQueries(Customer c, ServiceArea area, GeocodingOptions o)
    {
        var city = area.City;
        if (!string.IsNullOrWhiteSpace(c.Address))
            yield return (new GeocodeQuery(GeocodeQueryKind.Address, Compose(c.Address, c.Pincode, city, area.State), c.Pincode, city, area.State,
                area.CountryCode, area.Bounds), GeoMatchLevel.Address);

        var locality = locations.Classify(c.Address).Locality;
        if (!string.IsNullOrWhiteSpace(locality) && !string.Equals(locality, city, StringComparison.OrdinalIgnoreCase))
            yield return (new GeocodeQuery(GeocodeQueryKind.Address, Compose(locality, c.Pincode, city, area.State), c.Pincode, city, area.State,
                area.CountryCode, area.Bounds), GeoMatchLevel.Locality);

        if (o.AllowPincodeApproximation && !string.IsNullOrWhiteSpace(c.Pincode))
            yield return (new GeocodeQuery(GeocodeQueryKind.PostalCode, Compose(c.Pincode, null, city, area.State), c.Pincode, city, area.State,
                area.CountryCode, area.Bounds), GeoMatchLevel.Pincode);
    }

    /// <summary>The level stored for a match, or null when the match is too coarse to place a customer.</summary>
    private static GeoMatchLevel? MatchLevel(GeoMatchLevel queried, GeoPrecision found) => (queried, found) switch
    {
        (_, GeoPrecision.City or GeoPrecision.Region or GeoPrecision.Unknown) => null,
        (GeoMatchLevel.Address, GeoPrecision.Building or GeoPrecision.Street) => GeoMatchLevel.Address,
        (GeoMatchLevel.Address or GeoMatchLevel.Locality, GeoPrecision.Building or GeoPrecision.Street or GeoPrecision.Locality) => GeoMatchLevel.Locality,
        (GeoMatchLevel.Pincode, GeoPrecision.Building or GeoPrecision.Street) => null, // a pincode cannot be a street; ignore odd matches
        _ => GeoMatchLevel.Pincode
    };

    /// <summary>"address, pincode, city, state" without repeating parts the address already contains.</summary>
    private static string Compose(string text, string? pincode, string? city, string? state)
    {
        var parts = new List<string> { text.Trim().TrimEnd(',', '.') };
        foreach (var extra in new[] { pincode, city, state })
            if (!string.IsNullOrWhiteSpace(extra) && !text.Contains(extra, StringComparison.OrdinalIgnoreCase)) parts.Add(extra.Trim());
        return string.Join(", ", parts);
    }

    private static string? Truncate(string? value) =>
        value is null ? null : value.Length <= MaxMessageLength ? value : value[..MaxMessageLength];
}
