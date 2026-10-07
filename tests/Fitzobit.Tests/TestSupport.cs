using ClosedXML.Excel;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Catalogue;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Imports;
using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Imports.Plans;
using Fitzobit.Application.Imports.Validation;
using Fitzobit.Infrastructure.Catalogue;
using Fitzobit.Infrastructure.Imports;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
/// A real SQLite database (in-memory, kept alive by an open connection) built by running the actual
/// migrations, so constraints and indexes behave exactly as in data\fitzobit.db.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public FixedClock Clock { get; } = new(new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc));
    public TestUser User { get; } = new();

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public FitzobitDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<FitzobitDbContext>().UseSqlite(_connection).AddInterceptors(interceptors).Options, User, Clock);

    public IAuditLogger AuditFor(FitzobitDbContext db) => new AuditLogger(db, User, Clock);

    public void Dispose() => _connection.Dispose();
}

public static class TestPaths
{
    public static string RepositoryRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Fitzobit.sln"))) return dir.FullName;
            throw new InvalidOperationException("Could not locate Fitzobit.sln above the test output folder.");
        }
    }

    public static string CatalogueWorkbook => Path.Combine(RepositoryRoot, "Fitzobit_WhatsApp_Catalogue.xlsx");

    public static ICatalogueSource WorkbookSource() =>
        new ExcelCatalogueSource(Options.Create(new CatalogueSeedOptions
        {
            FilePath = CatalogueWorkbook,
            CategoryNames = new Dictionary<string, string>
            {
                ["Fit Box Subscription (monthly)"] = "Monthly Fit Box",
                ["Fit Box - 6 Days (weekly trial)"] = "6-Day Fit Box Trial",
                ["Standalone high Protein Salads"] = "Protein Salads",
                ["Oats & Muesli Breakfast"] = "Oats & Muesli"
            }
        }), NullLogger<ExcelCatalogueSource>.Instance);
}

/// <summary>Builds small .xlsx files in memory.</summary>
public static class TestWorkbook
{
    public static readonly string[] StandardHeaders = ["CustomerID", "CustomerName", "PhoneNumber", "Location", "Plan"];

    public static MemoryStream Create(string[] headers, params object?[][] rows) => Create("Customers", 1, headers, rows);

    public static MemoryStream Create(string sheetName, int headerRow, string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        for (var c = 0; c < headers.Length; c++) sheet.Cell(headerRow, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = sheet.Cell(headerRow + 1 + r, c + 1);
                switch (rows[r][c])
                {
                    case null: break;
                    case double d: cell.Value = d; break;
                    case long l: cell.Value = l; break;
                    case Formula f: cell.FormulaA1 = f.Text; break;
                    case var v: cell.Value = v.ToString(); break;
                }
            }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    public sealed record Formula(string Text);
}

/// <summary>Wires the real import services against a test database (catalogue seeded from the real workbook).</summary>
public sealed class ImportHarness
{
    public static readonly LocationClassificationOptions LocationOptions = new()
    {
        KnownCities = ["Thanjavur", "Coimbatore", "Chennai", "Madurai", "Tiruchirappalli", "Kumbakonam"],
        CityAliases = new(StringComparer.OrdinalIgnoreCase) { ["Trichy"] = "Tiruchirappalli", ["Tanjore"] = "Thanjavur" },
        RegionWords = ["Tamil Nadu", "TN", "India"]
    };

    private readonly TestDatabase _test;

    private ImportHarness(TestDatabase test) => _test = test;

    public static async Task<ImportHarness> CreateAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        await new CatalogueSeeder(db, TestPaths.WorkbookSource(), test.AuditFor(db), NullLogger<CatalogueSeeder>.Instance).SeedAsync();
        return new ImportHarness(test);
    }

    public ImportUploadService Upload(FitzobitDbContext db) =>
        new(db, new ExcelCustomerFileReader(Options.Create(new ImportOptions())),
            new CustomerRowValidator(new LocationClassificationService(LocationOptions)),
            new PlanCatalogue(db), _test.AuditFor(db), _test.User, _test.Clock, Options.Create(new ImportOptions()));

    public ImportQueryService Queries(FitzobitDbContext db) => new(db);
    public ImportWorkflowService Workflow(FitzobitDbContext db) => new(db, new PlanCatalogue(db), _test.Clock);
    public ImportCommitService Commit(FitzobitDbContext db) => new(db, _test.User, _test.Clock, _test.AuditFor(db), NullLogger<ImportCommitService>.Instance);

    public DeliveryGroupService Groups(FitzobitDbContext db) =>
        new(db, _test.AuditFor(db), new DeliveryGroupAssignmentService(db, _test.User, _test.Clock, _test.AuditFor(db)));

    public async Task<int> UploadAsync(MemoryStream file, string name = "customers.xlsx")
    {
        await using var db = _test.CreateContext();
        var result = await Upload(db).UploadAsync(file, name, file.Length);
        Assert.Equal("Validated", result.Status);
        return result.BatchId;
    }

    public async Task<List<ImportRowDto>> RowsAsync(int batchId, ImportRowFilter? filter = null)
    {
        await using var db = _test.CreateContext();
        return (await Queries(db).GetRowsAsync(batchId, filter ?? new ImportRowFilter(), 1, 500)).Items.ToList();
    }
}
