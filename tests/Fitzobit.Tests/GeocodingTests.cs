using Fitzobit.Application.Geocoding;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Geography;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>
/// Locating customers: address → locality → pincode area, only inside the service city, never a city-wide guess; provider
/// failures retried later without blocking saves.
/// </summary>
public class GeocodingTests
{
    private static async Task<int> CreateAsync(TestDatabase test, string phone = "9876543210", string pincode = "641002", string? address = null)
    {
        var request = test.NewCustomer(phone: phone, pincode: pincode);
        if (address is not null) request.Address = address;
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(request)).Id;
    }

    private static async Task<GeocodeBatchResult> ProcessAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        return await test.Geocoding(db).ProcessDueAsync(25);
    }

    private static async Task<Customer> LoadAsync(TestDatabase test, int id)
    {
        await using var db = test.CreateContext();
        return await db.Customers.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    [Fact]
    public async Task New_customers_wait_for_a_lookup_and_saving_never_calls_the_provider()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        test.Geocoder.Queries.Clear();

        var id = await CreateAsync(test);

        var customer = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Pending, (decimal?)null, (decimal?)null), (customer.GeocodeStatus, customer.Latitude, customer.Longitude));
        Assert.Empty(test.Geocoder.Queries);
    }

    [Fact]
    public async Task A_found_address_stores_coordinates_with_its_precision_and_searches_inside_the_city()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        test.Geocoder.Address("7 Main Road", 11.0071234, 76.9500987);

        var result = await ProcessAsync(test);

        Assert.Equal((1, 1), (result.Processed, result.Located));
        var c = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Located, 11.007123m, 76.950099m, GeoMatchLevel.Address, "Fake"),
            (c.GeocodeStatus, c.Latitude, c.Longitude, c.GeocodeMatchLevel, c.GeocodeSource));
        Assert.Equal(test.Clock.UtcNow, c.GeocodedAt);
        var query = test.Geocoder.Queries.Last();
        Assert.Equal("7 Main Road, RS Puram, Coimbatore, 641002, Tamil Nadu", query.Text);
        Assert.Equal(("in", test.Geocoder.CityBounds), (query.CountryCode, query.Bounds));
    }

    [Fact]
    public async Task A_locality_match_is_recorded_as_locality()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        test.Geocoder.Address("RS Puram", 11.0, 76.95, GeoPrecision.Locality);

        await ProcessAsync(test);

        var c = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Located, GeoMatchLevel.Locality), (c.GeocodeStatus, c.GeocodeMatchLevel));
    }

    [Fact]
    public async Task Without_an_address_match_the_pincode_area_is_used_and_marked_approximate()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        test.Geocoder.Pincode("641002", 11.012, 76.961);

        await ProcessAsync(test);

        var c = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Located, GeoMatchLevel.Pincode, 11.012m), (c.GeocodeStatus, c.GeocodeMatchLevel, c.Latitude));
        Assert.Contains(test.Geocoder.Queries, q => q.Kind == GeocodeQueryKind.PostalCode && q.PostalCode == "641002");
    }

    [Fact]
    public async Task Pincode_approximation_can_be_turned_off()
    {
        using var test = new TestDatabase();
        test.GeocodingOptions.AllowPincodeApproximation = false;
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        test.Geocoder.Pincode("641002", 11.012, 76.961);

        await ProcessAsync(test);

        Assert.Equal(GeocodeStatus.NotFound, (await LoadAsync(test, id)).GeocodeStatus);
        Assert.DoesNotContain(test.Geocoder.Queries, q => q.Kind == GeocodeQueryKind.PostalCode);
    }

    [Theory]
    [InlineData("city")]      // only the city centre matched: not a customer location
    [InlineData("outside")]   // a match in another city
    [InlineData("nothing")]   // no match at all
    public async Task Unusable_matches_leave_the_location_unavailable_instead_of_inventing_one(string scenario)
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        switch (scenario)
        {
            case "city": test.Geocoder.Address("Coimbatore", 11.0168, 76.9558, GeoPrecision.City); break;
            case "outside": test.Geocoder.Address("7 Main Road", 13.0827, 80.2707); break; // Chennai
        }

        var result = await ProcessAsync(test);

        Assert.Equal(1, result.NotFound);
        var c = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.NotFound, (decimal?)null, (decimal?)null, (GeoMatchLevel?)null),
            (c.GeocodeStatus, c.Latitude, c.Longitude, c.GeocodeMatchLevel));
        Assert.Contains("not found in Coimbatore", c.GeocodeMessage);
        if (scenario == "outside") Assert.Contains("outside Coimbatore", c.GeocodeMessage);
        Assert.Null(c.GeocodeNextAttemptAt); // no automatic retry for "not found"
    }

    [Fact]
    public async Task Provider_errors_are_retried_later_and_then_marked_failed()
    {
        using var test = new TestDatabase();
        test.GeocodingOptions.MaxAttempts = 3;
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        test.Geocoder.Outage = GeocodeResponse.Failed("the geocoding service could not be reached");

        Assert.Equal(1, (await ProcessAsync(test)).Retrying);
        var waiting = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Pending, 1, (decimal?)null), (waiting.GeocodeStatus, waiting.GeocodeAttempts, waiting.Latitude));
        Assert.Equal(test.Clock.UtcNow.AddMinutes(1), waiting.GeocodeNextAttemptAt);

        Assert.Equal(0, (await ProcessAsync(test)).Processed); // not due yet

        test.Clock.UtcNow = test.Clock.UtcNow.AddMinutes(2);
        await ProcessAsync(test);
        test.Clock.UtcNow = test.Clock.UtcNow.AddHours(1);
        Assert.Equal(1, (await ProcessAsync(test)).Failed);
        var failed = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Failed, 3, (DateTime?)null), (failed.GeocodeStatus, failed.GeocodeAttempts, failed.GeocodeNextAttemptAt));

        // A manual retry starts over and succeeds once the provider is back.
        test.Geocoder.Outage = null;
        test.Geocoder.Address("7 Main Road", 11.0, 76.95);
        await using (var db = test.CreateContext())
            Assert.Equal(1, await test.Geocoding(db).RequestAsync([id]));
        await ProcessAsync(test);
        Assert.Equal(GeocodeStatus.Located, (await LoadAsync(test, id)).GeocodeStatus);
    }

    [Fact]
    public async Task Rate_limiting_stops_the_batch_so_the_provider_is_not_hammered()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        for (var i = 0; i < 3; i++) await CreateAsync(test, phone: $"981234567{i}");
        test.Geocoder.Queries.Clear();
        test.Geocoder.Outage = GeocodeResponse.RateLimited("slow down");

        var result = await ProcessAsync(test);

        Assert.Equal((1, 1), (result.Processed, result.Retrying));
        Assert.Single(test.Geocoder.Queries);
    }

    [Fact]
    public async Task Nothing_is_looked_up_without_a_service_city_or_a_provider()
    {
        using var test = new TestDatabase();
        await CreateAsync(test);

        var noCity = await ProcessAsync(test);
        Assert.Contains("service city", noCity.Skipped);

        await test.SetServiceCityAsync();
        test.Geocoder.IsConfigured = false;
        test.Geocoder.Queries.Clear();
        var noProvider = await ProcessAsync(test);
        Assert.Contains("not configured", noProvider.Skipped);
        Assert.Empty(test.Geocoder.Queries);
    }

    [Fact]
    public async Task Changing_the_address_forgets_the_old_coordinates_but_other_edits_keep_them()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync();
        var id = await CreateAsync(test);
        test.Geocoder.Address("7 Main Road", 11.0, 76.95);
        await ProcessAsync(test);

        await using (var db = test.CreateContext())
        {
            var detail = await test.Queries(db).GetDetailAsync(id);
            await test.Commands(db).UpdateAsync(id, CustomerTests.Update(detail, r => r.CustomerName = "Renamed Customer"));
        }
        Assert.Equal(GeocodeStatus.Located, (await LoadAsync(test, id)).GeocodeStatus);

        await using (var db = test.CreateContext())
        {
            var detail = await test.Queries(db).GetDetailAsync(id);
            await test.Commands(db).UpdateAsync(id, CustomerTests.Update(detail, r => r.Address = "22 Cross Cut Road, Gandhipuram, Coimbatore"));
        }
        var moved = await LoadAsync(test, id);
        Assert.Equal((GeocodeStatus.Pending, (decimal?)null, 0), (moved.GeocodeStatus, moved.Latitude, moved.GeocodeAttempts));
    }

    [Fact]
    public async Task Saving_the_service_city_stores_its_area()
    {
        using var test = new TestDatabase();
        await test.SetServiceCityAsync("Coimbatore", new GeoBounds(10.9, 76.8, 11.2, 77.1));

        await using var db = test.CreateContext();
        var area = await test.Area(db).GetAsync();
        Assert.Equal(("Coimbatore", "Tamil Nadu", true, true), (area.City, area.State, area.IsConfigured, area.AreaFound));
        Assert.Equal(11.05, area.CenterLatitude!.Value, 6);

        test.Geocoder.CityBounds = null;
        var unknown = await test.Area(db).SaveAsync(new SaveServiceAreaRequest { City = "Nowhere" });
        Assert.False(unknown.AreaFound);
        Assert.Contains("not found", unknown.AreaMessage);
    }

    [Fact]
    public void Service_area_bounds_include_a_margin()
    {
        var bounds = new GeoBounds(11.0, 76.9, 11.1, 77.0);
        Assert.True(bounds.Contains(new GeoPoint(11.05, 76.95)));
        Assert.False(bounds.Contains(new GeoPoint(11.15, 76.95)));
        Assert.True(bounds.Contains(new GeoPoint(11.15, 76.95), marginKm: 10)); // 0.05° ≈ 5.5 km outside
        Assert.Equal(bounds, GeoBounds.Parse(bounds.Serialize()));
    }
}
