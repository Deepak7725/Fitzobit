using System.Net;
using System.Text;
using Fitzobit.Application.Geocoding;
using Fitzobit.Infrastructure.Geocoding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fitzobit.Tests;

/// <summary>The Nominatim and Google adapters, against canned HTTP answers (no network).</summary>
public class GeocodingProviderTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network down");
    }

    private static readonly GeoBounds Coimbatore = new(10.90, 76.85, 11.13, 77.10);

    private static GeocodeQuery Address(string text) => new(GeocodeQueryKind.Address, text, "641002", "Coimbatore", "Tamil Nadu", "in", Coimbatore);

    private static (NominatimGeocodingService Service, FakeHandler Handler) Nominatim(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        var options = Options.Create(new GeocodingOptions { Nominatim = { MinIntervalMs = 0, ContactEmail = "ops@example.com" } });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.example/") };
        return (new NominatimGeocodingService(http, new NominatimRequestGate(options), options, NullLogger<NominatimGeocodingService>.Instance), handler);
    }

    [Fact]
    public async Task Nominatim_house_match_is_parsed_and_the_search_is_limited_to_the_city()
    {
        var (service, handler) = Nominatim(HttpStatusCode.OK, """
            [{"lat":"11.0071","lon":"76.9501","display_name":"7, Main Road, RS Puram, Coimbatore","place_rank":30,"addresstype":"house",
              "boundingbox":["11.0070","11.0072","76.9500","76.9502"]}]
            """);

        var result = await service.GeocodeAsync(Address("7 Main Road, RS Puram, Coimbatore"));

        Assert.Equal((GeocodeOutcome.Found, GeoPrecision.Building, 11.0071, 76.9501), (result.Outcome, result.Precision, result.Point!.Value.Latitude, result.Point.Value.Longitude));
        var query = Uri.UnescapeDataString(handler.Requests.Single().Query);
        Assert.Contains("countrycodes=in", query);
        Assert.Contains("viewbox=76.85,11.13,77.1,10.9", query);
        Assert.Contains("bounded=1", query);
        Assert.Contains("email=ops@example.com", query);
    }

    [Theory]
    [InlineData("""[{"lat":"11","lon":"77","place_rank":20,"addresstype":"suburb"}]""", GeoPrecision.Locality)]
    [InlineData("""[{"lat":"11","lon":"77","place_rank":26,"addresstype":"road"}]""", GeoPrecision.Street)]
    [InlineData("""[{"lat":"11","lon":"77","place_rank":21,"addresstype":"postcode"}]""", GeoPrecision.PostalCode)]
    [InlineData("""[{"lat":"11","lon":"77","place_rank":16,"addresstype":"city"}]""", GeoPrecision.City)]
    [InlineData("""[{"lat":"11","lon":"77","place_rank":8,"addresstype":"state"}]""", GeoPrecision.Region)]
    public async Task Nominatim_precision_follows_the_matched_place_type(string body, GeoPrecision expected)
    {
        var (service, _) = Nominatim(HttpStatusCode.OK, body);
        Assert.Equal(expected, (await service.GeocodeAsync(Address("x"))).Precision);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "[]", GeocodeOutcome.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, "", GeocodeOutcome.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", GeocodeOutcome.Failed)]
    [InlineData(HttpStatusCode.OK, "not json", GeocodeOutcome.Failed)]
    public async Task Nominatim_problems_never_produce_coordinates(HttpStatusCode status, string body, GeocodeOutcome expected)
    {
        var (service, _) = Nominatim(status, body);
        var result = await service.GeocodeAsync(Address("x"));
        Assert.Equal((expected, (Domain.Geography.GeoPoint?)null), (result.Outcome, result.Point));
    }

    [Fact]
    public async Task Nominatim_network_errors_are_reported_as_failures()
    {
        var options = Options.Create(new GeocodingOptions { Nominatim = { MinIntervalMs = 0 } });
        var service = new NominatimGeocodingService(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://nominatim.example/") },
            new NominatimRequestGate(options), options, NullLogger<NominatimGeocodingService>.Instance);
        Assert.Equal(GeocodeOutcome.Failed, (await service.GeocodeAsync(Address("x"))).Outcome);
    }

    [Fact]
    public async Task Nominatim_pincode_lookups_use_the_structured_postal_code_search()
    {
        var (service, handler) = Nominatim(HttpStatusCode.OK, "[]");
        await service.GeocodeAsync(new GeocodeQuery(GeocodeQueryKind.PostalCode, "641002, Coimbatore", "641002", "Coimbatore", null, "in", null));
        var query = Uri.UnescapeDataString(handler.Requests.Single().Query);
        Assert.Contains("postalcode=641002", query);
        Assert.DoesNotContain("q=", query);
    }

    [Fact]
    public async Task Google_needs_a_key_and_parses_results_without_leaking_it()
    {
        var noKey = Options.Create(new GeocodingOptions());
        var unconfigured = new GoogleGeocodingService(new HttpClient(new FakeHandler(HttpStatusCode.OK, "{}")), new GoogleRequestGate(noKey), noKey,
            NullLogger<GoogleGeocodingService>.Instance);
        Assert.False(unconfigured.IsConfigured);
        Assert.Equal(GeocodeOutcome.NotConfigured, (await unconfigured.GeocodeAsync(Address("x"))).Outcome);

        var options = Options.Create(new GeocodingOptions { Google = { ApiKey = "secret-key", MinIntervalMs = 0 } });
        var handler = new FakeHandler(HttpStatusCode.OK, """
            {"status":"OK","results":[{"formatted_address":"7 Main Rd, R.S. Puram, Coimbatore","types":["street_address"],
              "geometry":{"location":{"lat":11.0071,"lng":76.9501},"location_type":"ROOFTOP",
                          "viewport":{"northeast":{"lat":11.008,"lng":76.951},"southwest":{"lat":11.006,"lng":76.949}}}}]}
            """);
        var google = new GoogleGeocodingService(new HttpClient(handler) { BaseAddress = new Uri("https://maps.example/geocode/json") }, new GoogleRequestGate(options),
            options, NullLogger<GoogleGeocodingService>.Instance);
        var found = await google.GeocodeAsync(Address("7 Main Road, RS Puram"));
        Assert.Equal((GeocodeOutcome.Found, GeoPrecision.Building, 11.0071), (found.Outcome, found.Precision, found.Point!.Value.Latitude));
        Assert.Contains("components=country%3AIN", handler.Requests.Single().Query);

        var denied = new FakeHandler(HttpStatusCode.OK, """{"status":"REQUEST_DENIED","error_message":"bad key secret-key"}""");
        var failing = new GoogleGeocodingService(new HttpClient(denied) { BaseAddress = new Uri("https://maps.example/geocode/json") }, new GoogleRequestGate(options),
            options, NullLogger<GoogleGeocodingService>.Instance);
        var result = await failing.GeocodeAsync(Address("x"));
        Assert.Equal(GeocodeOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("secret-key", result.Message);
    }
}
