using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Customers;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Classification;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Application.Geocoding;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Geography;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fitzobit.Tests;

public sealed class TestUser : ICurrentUser
{
    public string UserName => "test-user";
    public string DisplayName => "Test User";
    public string Role => "Manager";
}

public sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}

/// <summary>
/// A real PostgreSQL database built by running the actual migrations (which also seed the drop-down values), so
/// constraints, indexes and sequences behave exactly as in production. Every test gets its own throw-away database.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly PostgresTestDatabase _postgres = new();

    /// <summary>Wednesday 7 October 2026, 14:30 in India.</summary>
    public FixedClock Clock { get; } = new(new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc));
    public TestUser User { get; } = new();

    /// <summary>Geocoding provider used by the services below (no network in tests).</summary>
    public FakeGeocoder Geocoder { get; } = new();
    public GeocodingQueue Queue { get; } = new();
    public GeocodingOptions GeocodingOptions { get; } = new();

    public TestDatabase(bool migrate = true)
    {
        if (!migrate) return;
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public string ConnectionString => _postgres.ConnectionString;

    public FitzobitDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseConfigurator.Configure(builder, _postgres.ConnectionString);
        return new FitzobitDbContext(builder.AddInterceptors(interceptors).Options, User, Clock);
    }

    public IAuditLogger AuditFor(FitzobitDbContext db) => new AuditLogger(db, User, Clock);

    public DictionaryService Dictionaries(FitzobitDbContext db) => new(db, AuditFor(db));

    public DeliveryScheduleService Schedule(FitzobitDbContext db) => new(new DeliveryCalendarService(), Dictionaries(db), Clock);

    public CustomerCommandService Commands(FitzobitDbContext db) =>
        new(db, new LocationClassificationService(LocationOptions), Dictionaries(db), Packages(db), new SequenceCustomerIdGenerator(db), Queue, AuditFor(db));

    public CustomerPackageService Packages(FitzobitDbContext db) => new(db, Dictionaries(db), Schedule(db), AuditFor(db), Clock);

    public CustomerNoteService Notes(FitzobitDbContext db) => new(db, AuditFor(db), User);

    public CustomerPauseService Pauses(FitzobitDbContext db) => new(db, Schedule(db), AuditFor(db), User, Clock);

    public CustomerQueryService Queries(FitzobitDbContext db) => new(db, new DeliveryCalendarService(), Clock, User);

    public DeliveryGroupAssignmentService Assignments(FitzobitDbContext db) => new(db, User, Clock, AuditFor(db));

    public DeliveryGroupService Groups(FitzobitDbContext db) => new(db, AuditFor(db), Assignments(db));

    public ServiceAreaService Area(FitzobitDbContext db) => new(db, Geocoder, Queue, AuditFor(db), User, Options.Create(GeocodingOptions));

    public CustomerGeocodingService Geocoding(FitzobitDbContext db) =>
        new(db, Geocoder, Area(db), new LocationClassificationService(LocationOptions), Queue, AuditFor(db), Clock, Options.Create(GeocodingOptions),
            NullLogger<CustomerGeocodingService>.Instance);

    public ClassificationService Classification(FitzobitDbContext db) => new(db, Groups(db), Assignments(db), Area(db), AuditFor(db));

    /// <summary>Sets the service city; the fake geocoder answers the city lookup with <paramref name="bounds"/>.</summary>
    public async Task SetServiceCityAsync(string city = "Coimbatore", GeoBounds? bounds = null)
    {
        Geocoder.CityBounds = bounds ?? new GeoBounds(10.90, 76.85, 11.13, 77.10);
        await using var db = CreateContext();
        await Area(db).SaveAsync(new SaveServiceAreaRequest { City = city, State = "Tamil Nadu" });
    }

    /// <summary>Id of a seeded drop-down value, e.g. Entry(DictionaryType.Frequency, "WEEKLY").</summary>
    public int Entry(DictionaryType type, string code)
    {
        using var db = CreateContext();
        return db.DictionaryEntries.Where(e => e.DictionaryType == type && e.Code == code).Select(e => e.Id).Single();
    }

    /// <summary>A valid Add Customer request: delivery at 07:30 and one package (weekly, Veg, Medium ×1, starting Monday 12 Oct 2026).</summary>
    public CreateCustomerRequest NewCustomer(string name = "Anitha Raman", string phone = "9876543210", string pincode = "641002",
        string frequency = "WEEKLY", DateOnly? start = null, string? note = null) => new()
    {
        CustomerName = name,
        PhoneNumber = phone,
        Address = "7 Main Road, RS Puram, Coimbatore",
        Pincode = pincode,
        PreferredDeliveryTime = new TimeOnly(7, 30),
        Packages = [NewPackage(frequency: frequency, start: start)],
        Note = note
    };

    /// <summary>A valid package (Veg, Medium ×1, weekly by default, starting Monday 12 Oct 2026).</summary>
    public SavePackageRequest NewPackage(string frequency = "WEEKLY", DateOnly? start = null, string mealType = "MEDIUM", string preference = "VEG",
        int quantity = 1) => new()
    {
        PreferenceId = Entry(DictionaryType.Preference, preference),
        MealTypeId = Entry(DictionaryType.MealType, mealType),
        Quantity = quantity,
        FrequencyId = Entry(DictionaryType.Frequency, frequency),
        StartDate = start ?? new DateOnly(2026, 10, 12)
    };

    public static readonly LocationClassificationOptions LocationOptions = new()
    {
        KnownCities = ["Thanjavur", "Coimbatore", "Chennai", "Madurai", "Tiruchirappalli", "Kumbakonam"],
        CityAliases = new(StringComparer.OrdinalIgnoreCase) { ["Trichy"] = "Tiruchirappalli", ["Tanjore"] = "Thanjavur" },
        RegionWords = ["Tamil Nadu", "TN", "India"]
    };

    public void Dispose() => _postgres.Dispose();
}

/// <summary>
/// A uniquely named PostgreSQL database for one test, dropped afterwards. The server comes from FITZOBIT_TEST_PG (a connection
/// string allowed to create databases) or, when that is not set, from the app's local user secret Database:ConnectionString.
/// </summary>
public sealed class PostgresTestDatabase : IDisposable
{
    private static readonly Lazy<string> AdminConnectionString = new(ResolveAdminConnectionString);
    private readonly string _name = $"fitzobit_test_{Guid.NewGuid():N}";

    public string ConnectionString { get; }

    public PostgresTestDatabase()
    {
        Execute($"CREATE DATABASE \"{_name}\"");
        ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString.Value) { Database = _name }.ConnectionString;
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        Execute($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)");
    }

    private static void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(AdminConnectionString.Value);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static string ResolveAdminConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("FITZOBIT_TEST_PG");
        if (string.IsNullOrWhiteSpace(configured))
            configured = new ConfigurationBuilder().AddUserSecrets("fitzobit-web-local").Build()["Database:ConnectionString"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Tests need PostgreSQL. Set FITZOBIT_TEST_PG (e.g. Host=localhost;Username=postgres;Password=…;Database=postgres) " +
                "or the app's user secret Database:ConnectionString (see README, \"Local PostgreSQL\").");
        // Connect to the maintenance database to create and drop the per-test databases.
        return new NpgsqlConnectionStringBuilder(DatabaseConfigurator.ToNpgsqlConnectionString(configured)) { Database = "postgres", Pooling = true }
            .ConnectionString;
    }
}

/// <summary>
/// A scripted geocoding provider: answers from <see cref="Answers"/> (matched on the query text or pincode), records every
/// query, and can simulate outages. Unknown queries are "not found".
/// </summary>
public sealed class FakeGeocoder : IGeocodingService
{
    public string ProviderName => "Fake";
    public bool IsConfigured { get; set; } = true;
    public GeoBounds? CityBounds { get; set; }

    /// <summary>When set, every lookup returns this (e.g. Failed or RateLimited).</summary>
    public GeocodeResponse? Outage { get; set; }

    public List<GeocodeQuery> Queries { get; } = [];

    /// <summary>Key: text contained in the query (address part) or "pin:641002" for pincode lookups.</summary>
    public Dictionary<string, GeocodeResponse> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Address(string contains, double lat, double lng, GeoPrecision precision = GeoPrecision.Building) =>
        Answers[contains] = new GeocodeResponse(GeocodeOutcome.Found, new GeoPoint(lat, lng), precision, $"Match for {contains}");

    public void Pincode(string pincode, double lat, double lng) =>
        Answers["pin:" + pincode] = new GeocodeResponse(GeocodeOutcome.Found, new GeoPoint(lat, lng), GeoPrecision.PostalCode, $"Pincode {pincode}");

    public Task<GeocodeResponse> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        if (!IsConfigured) return Task.FromResult(new GeocodeResponse(GeocodeOutcome.NotConfigured));
        if (Outage is not null) return Task.FromResult(Outage);
        if (query.Kind == GeocodeQueryKind.City)
            return Task.FromResult(CityBounds is null
                ? GeocodeResponse.NotFound()
                : new GeocodeResponse(GeocodeOutcome.Found, CityBounds.Center, GeoPrecision.City, query.Text, CityBounds));
        if (query.Kind == GeocodeQueryKind.PostalCode)
            return Task.FromResult(Answers.GetValueOrDefault("pin:" + query.PostalCode) ?? GeocodeResponse.NotFound());
        var hit = Answers.FirstOrDefault(a => !a.Key.StartsWith("pin:") && query.Text.Contains(a.Key, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hit.Value ?? GeocodeResponse.NotFound());
    }
}
