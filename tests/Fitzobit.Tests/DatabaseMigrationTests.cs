using Fitzobit.Domain.Dictionaries;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Fitzobit.Tests;

/// <summary>PostgreSQL configuration, the migrated schema, and moving data from the old plan/import model safely.</summary>
public class DatabaseMigrationTests
{
    private const string InitialMigration = "20261008024903_InitialPostgreSql";

    [Fact]
    public void The_app_uses_postgresql_with_one_set_of_migrations()
    {
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseConfigurator.Configure(builder, "Host=localhost;Database=x;Username=u");
        using var db = new FitzobitDbContext(builder.Options, new TestUser(), new FixedClock(DateTime.UtcNow));

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Equal([InitialMigration, "20261008092400_CustomerDictionariesAndDeliveryDates", "20261008100654_DeliveryGroupNumbersAndGeocoding",
                "20261016094926_CustomerPackagesNotesAndDeliveryTime", "20261019063025_AlternatePhoneAndPausableFrequencies"],
            db.Database.GetMigrations());
        var activeIndex = db.Model.FindEntityType(typeof(Domain.DeliveryGroups.CustomerDeliveryGroupAssignment))!
            .GetIndexes().Single(i => i.GetDatabaseName() == "UX_CustomerDeliveryGroupAssignments_ActiveCustomer");
        Assert.Equal("\"IsActive\" = TRUE", activeIndex.GetFilter());
    }

    [Fact]
    public void A_missing_connection_string_explains_how_to_configure_it()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseConfigurator.Configure(new DbContextOptionsBuilder<FitzobitDbContext>(), " "));
        Assert.Contains("user-secrets", error.Message);
        Assert.Contains("Database__ConnectionString", error.Message);
    }

    [Theory]
    [InlineData("postgresql://alex:p%40ss%3Aword@ep-cool-1.ap-southeast-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require",
        "ep-cool-1.ap-southeast-1.aws.neon.tech", 5432, "neondb", "alex", "p@ss:word", SslMode.Require)]
    [InlineData("postgres://u:pw@db.example.com:6543/app", "db.example.com", 6543, "app", "u", "pw", SslMode.Prefer)]
    public void Hosted_postgresql_urls_become_npgsql_connection_strings(string url, string host, int port, string database, string user,
        string password, SslMode ssl)
    {
        var csb = new NpgsqlConnectionStringBuilder(DatabaseConfigurator.ToNpgsqlConnectionString(url));
        Assert.Equal((host, port, database, user, password, ssl), (csb.Host, csb.Port, csb.Database, csb.Username, csb.Password, csb.SslMode));
        Assert.Equal("Host=h;Database=d", DatabaseConfigurator.ToNpgsqlConnectionString("Host=h;Database=d"));
        Assert.Equal($"{database} on {host}", DatabaseConfigurator.Describe(url)); // never the user or password
    }

    [Fact]
    public async Task Migrations_create_the_expected_schema()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var tables = await Strings(db, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");
        foreach (var table in new[] { "Customers", "CustomerPackages", "PackagePauses", "CustomerNotes", "DictionaryEntries", "DeliveryGroups", "CustomerDeliveryGroupAssignments", "AppSettings",
                     "CustomerDeliveryGroupAssignmentHistory", "AuditLogs", "Users", "DataProtectionKeys" })
            Assert.Contains(table, tables);
        foreach (var retired in new[] { "Plans", "CatalogueItems", "CataloguePricingTiers", "ImportBatches", "ImportRows", "PlanAliases", "CustomerPauses" })
            Assert.DoesNotContain(retired, tables);

        var columns = await Strings(db, "SELECT column_name FROM information_schema.columns WHERE table_name = 'Customers'");
        Assert.Superset(new HashSet<string> { "CustomerId", "CustomerName", "PhoneNumber", "Address", "Pincode", "PreferredDeliveryTime", "StatusId",
            "Latitude", "Longitude", "GeocodeStatus", "GeocodeMatchLevel", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy" },
            columns.ToHashSet());
        foreach (var moved in new[] { "PlanId", "Location", "PreferenceId", "MealTypeId", "FrequencyId", "StartDate", "EndDate", "PausedCount" })
            Assert.DoesNotContain(moved, columns);
        Assert.Equal("time without time zone",
            (await Strings(db, "SELECT data_type FROM information_schema.columns WHERE table_name = 'Customers' AND column_name = 'PreferredDeliveryTime'")).Single());
        var packageColumns = await Strings(db, "SELECT column_name FROM information_schema.columns WHERE table_name = 'CustomerPackages'");
        Assert.Superset(new HashSet<string> { "CustomerId", "PreferenceId", "MealTypeId", "Quantity", "FrequencyId", "StartDate", "EndDate", "PausedCount" },
            packageColumns.ToHashSet());

        var indexes = await Strings(db, "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");
        foreach (var index in new[] { "IX_Customers_CustomerId", "IX_Customers_StatusId", "IX_CustomerPackages_CustomerId_StartDate",
                     "IX_CustomerPackages_EndDate", "IX_CustomerPackages_MealTypeId", "IX_DictionaryEntries_DictionaryType_Code",
                     "IX_PackagePauses_PackageId_PausedDate", "IX_CustomerNotes_CustomerId_CreatedAt", "UX_CustomerDeliveryGroupAssignments_ActiveCustomer",
                     "IX_DeliveryGroups_GroupNumber", "IX_Customers_GeocodeStatus_GeocodeNextAttemptAt" })
            Assert.Contains(index, indexes);

        var checks = await Strings(db, "SELECT conname FROM pg_constraint WHERE contype = 'c'");
        Assert.Superset(new HashSet<string> { "CK_CustomerPackages_PausedCount", "CK_CustomerPackages_Quantity", "CK_CustomerPackages_Dates" },
            checks.ToHashSet());
        Assert.DoesNotContain("CK_Customers_PausedCount", checks);
        Assert.Contains("CustomerNumberSequence", await Strings(db, "SELECT sequence_name FROM information_schema.sequences"));
    }

    [Fact]
    public async Task Upgrading_keeps_existing_customers_and_archives_the_old_model()
    {
        using var test = new TestDatabase(migrate: false);
        await using (var db = test.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(InitialMigration);
            await db.Database.ExecuteSqlRawAsync(OldModelData);
        }

        await using (var db = test.CreateContext())
            await db.Database.MigrateAsync();

        await using var check = test.CreateContext();
        var customers = await check.Customers.AsNoTracking().Include(c => c.Status).OrderBy(c => c.CustomerId).ToListAsync();

        Assert.Equal(["CUST001", "CUST002", "CUST003", "FITZ-000007"], customers.Select(c => c.CustomerId));
        var veg = customers[0];
        Assert.Equal(("Anitha Raman", "9876543210", "12 RS Puram, Coimbatore 641002", "641002", "Coimbatore"),
            (veg.CustomerName, veg.PhoneNumber, veg.Address, veg.Pincode, veg.City));
        Assert.Equal("Active", veg.Status!.DisplayName);
        Assert.Equal("Medium Fit Box Subscription (5 Items) - monthly · Veg Plan · from file: Medium Box Veg", veg.LegacyPlan);
        Assert.Equal("Inactive", customers[1].Status!.DisplayName);
        Assert.Equal(("Active", (string?)null), (customers[2].Status!.DisplayName, customers[2].LegacyPlan));

        // "Start fresh" (owner's decision): the old single package per customer is not converted; packages and the delivery time are
        // entered again, so every carried-over customer shows as incomplete.
        Assert.Equal(0, await check.CustomerPackages.CountAsync());
        Assert.All(customers, c => Assert.Null(c.PreferredDeliveryTime));
        Assert.Equal(4, (await test.Queries(check).ListAsync(new Application.Customers.CustomerFilter { Incomplete = true }, 1, 10)).Total);

        // Group assignments and history are untouched; the group is numbered and keeps its old name in the notes.
        Assert.Equal(2, await check.CustomerDeliveryGroupAssignments.CountAsync(a => a.IsActive));
        var group = await check.DeliveryGroups.SingleAsync();
        Assert.Equal((1, "Previously named: Coimbatore North"), (group.GroupNumber, group.Description));
        Assert.All(customers, c => Assert.Equal(Domain.Customers.GeocodeStatus.Pending, c.GeocodeStatus));
        Assert.Equal(1, await check.CustomerDeliveryGroupAssignmentHistory.CountAsync());

        // The retired tables and every customer's original plan/tier/import ids are archived, not deleted.
        Assert.Equal(2L, await Scalar(check, "SELECT count(*) FROM legacy.\"Plans\""));
        Assert.Equal(1L, await Scalar(check, "SELECT count(*) FROM legacy.\"ImportRows\""));
        Assert.Equal(4L, await Scalar(check, "SELECT count(*) FROM legacy.\"CustomerPlans\""));
        Assert.Equal(1L, await Scalar(check, "SELECT count(*) FROM legacy.\"CustomerPlans\" WHERE \"PricingTierId\" IS NOT NULL AND \"SourceImportBatchId\" = 1"));

        // New ids continue after the highest existing FITZ-nnnnnn id.
        var created = await test.Commands(check).CreateAsync(test.NewCustomer());
        Assert.Equal("FITZ-000008", created.CustomerId);
        Assert.Equal(DictionaryType.CustomerStatus, (await check.DictionaryEntries.FindAsync(veg.StatusId))!.DictionaryType);
    }

    /// <summary>Rows as the previous version of the app stored them (InitialPostgreSql schema).</summary>
    private const string OldModelData = """
        INSERT INTO "CatalogueItems" ("Id", "Name", "DisplayPrice", "Currency", "BillingPeriod", "DisplayOrder", "CreatedAt", "UpdatedAt")
        OVERRIDING SYSTEM VALUE VALUES
            (1, 'Medium Fit Box Subscription (5 Items) - monthly', 699, 'INR', 'Monthly', 1, now(), now()),
            (2, 'Standalone high Protein Salads - weekly - 6 days', 2299, 'INR', 'Weekly', 2, now(), now());
        INSERT INTO "CataloguePricingTiers" ("Id", "CatalogueItemId", "TierName", "Price", "Currency", "BillingPeriod")
        OVERRIDING SYSTEM VALUE VALUES (1, 1, 'Veg Plan', 2699, 'INR', 'Monthly');
        INSERT INTO "Plans" ("Id", "Name", "Code", "Status", "CatalogueItemId", "CreatedAt", "UpdatedAt")
        OVERRIDING SYSTEM VALUE VALUES
            (1, 'Medium Fit Box Subscription (5 Items) - monthly', 'MEDIUM-MONTHLY', 'Active', 1, now(), now()),
            (2, 'Standalone high Protein Salads - weekly - 6 days', 'SALADS-WEEKLY', 'Active', 2, now(), now());
        INSERT INTO "ImportBatches" ("Id", "FileName", "FileSizeBytes", "UploadedAt", "TotalRows", "ValidRows", "InvalidRows", "WarningRows",
            "EmptyRowsSkipped", "ImportedCount", "CustomersCreated", "ExistingCustomersMatched", "GroupsCreated", "AssignmentsChanged", "Status",
            "ValidationDurationMs", "SaveDurationMs", "CreatedAt")
        OVERRIDING SYSTEM VALUE VALUES (1, 'customers.xlsx', 1000, now(), 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 'Completed', 1, 1, now());
        INSERT INTO "ImportRows" ("ImportBatchId", "RowNumber", "CustomerId", "Status", "IssuesJson", "IssueCodes", "IsExistingCustomer",
            "IsPotentialDuplicate", "ExcludedByManager")
        VALUES (1, 2, 'CUST001', 'Valid', '[]', ';', false, false, false);
        INSERT INTO "Customers" ("Id", "CustomerId", "CustomerName", "PhoneNumber", "NormalizedPhoneNumber", "Location", "NormalizedLocation",
            "Pincode", "City", "OriginalPlan", "PlanId", "PricingTierId", "SourceImportBatchId", "Status", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
        OVERRIDING SYSTEM VALUE VALUES
            (1, 'CUST001', 'Anitha Raman', '9876543210', '9876543210', '12 RS Puram, Coimbatore 641002', '12 rs puram, coimbatore 641002',
                '641002', 'Coimbatore', 'Medium Box Veg', 1, 1, 1, 'Active', now(), now(), 'owner', 'owner'),
            (2, 'CUST002', 'Karthik N', '9123456789', '9123456789', 'Gandhipuram, Coimbatore', 'gandhipuram, coimbatore',
                NULL, 'Coimbatore', 'Protein Salads weekly', 2, NULL, NULL, 'Inactive', now(), now(), 'owner', 'owner'),
            (3, 'CUST003', 'No Plan Person', '9000000003', '9000000003', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'Active', now(), now(), NULL, NULL),
            (4, 'FITZ-000007', 'Looks Generated', '9000000004', '9000000004', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'Active', now(), now(), NULL, NULL);
        SELECT setval(pg_get_serial_sequence('"Customers"', 'Id'), 100);
        INSERT INTO "DeliveryGroups" ("Id", "Name", "Status", "CreatedAt", "UpdatedAt")
        OVERRIDING SYSTEM VALUE VALUES (1, 'Coimbatore North', 'Active', now(), now());
        INSERT INTO "CustomerDeliveryGroupAssignments" ("CustomerId", "DeliveryGroupId", "AssignedAt", "IsActive")
        VALUES (1, 1, now(), true), (2, 1, now(), true);
        INSERT INTO "CustomerDeliveryGroupAssignmentHistory" ("CustomerId", "NewGroupId", "ChangedAt", "Reason") VALUES (1, 1, now(), 'Import');
        """;

    private static async Task<List<string>> Strings(FitzobitDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<string>($"SELECT x::text AS \"Value\" FROM ({sql}) AS q(x)").ToListAsync();

    private static async Task<long> Scalar(FitzobitDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<long>($"SELECT ({sql}) AS \"Value\"").SingleAsync();
}
