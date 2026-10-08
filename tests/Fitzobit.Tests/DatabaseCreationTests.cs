using Fitzobit.Application.Abstractions;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

public class DatabaseCreationTests
{
    private static readonly string[] ExpectedTables =
    [
        "AuditLogs", "CatalogueItems", "CataloguePricingTiers", "CustomerDeliveryGroupAssignmentHistory",
        "CustomerDeliveryGroupAssignments", "Customers", "DeliveryGroups", "ImportBatches", "Plans"
    ];

    [Fact]
    public async Task Migrations_create_a_sqlite_file_with_the_expected_schema()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fitzobit-tests", Guid.NewGuid().ToString("N"));
        var options = new DatabaseOptions { ConnectionString = "Data Source=data/test.db;Pooling=False" };
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseProviderConfigurator.Configure(builder, options, folder);

        try
        {
            await using (var db = new FitzobitDbContext(builder.Options, new TestUser(), new SystemClock()))
            {
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
            }

            var file = Path.Combine(folder, "data", "test.db");
            Assert.True(File.Exists(file), "The relative Data Source should resolve under the base path and be created.");

            await using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
            await connection.OpenAsync();
            var tables = await QueryStrings(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
            foreach (var table in ExpectedTables) Assert.Contains(table, tables);

            var indexes = await QueryStrings(connection, "SELECT name FROM sqlite_master WHERE type = 'index'");
            Assert.Contains("IX_Customers_CustomerId", indexes);
            Assert.Contains("IX_Customers_NormalizedPhoneNumber", indexes);
            Assert.Contains("IX_Customers_Pincode", indexes);
            Assert.Contains("IX_Customers_PlanId", indexes);
            Assert.Contains("IX_Customers_Status", indexes);
            Assert.Contains("IX_Customers_CreatedAt", indexes);
            Assert.Contains("IX_CustomerDeliveryGroupAssignments_DeliveryGroupId_IsActive", indexes);
            Assert.Contains("IX_CustomerDeliveryGroupAssignments_CustomerId_IsActive", indexes);
            Assert.Contains("UX_CustomerDeliveryGroupAssignments_ActiveCustomer", indexes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void PostgreSql_uses_its_own_migrations_and_the_same_model()
    {
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseProviderConfigurator.Configure(builder,
            new DatabaseOptions { Provider = DatabaseProviders.PostgreSql, ConnectionString = "Host=localhost;Database=x;Username=u" }, Path.GetTempPath());
        using var db = new FitzobitDbContext(builder.Options, new TestUser(), new FixedClock(DateTime.UtcNow));

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        var migrations = db.Database.GetMigrations().ToList();
        Assert.Contains(migrations, m => m.EndsWith("_InitialPostgreSql"));
        Assert.DoesNotContain(migrations, m => m.EndsWith("_InitialCreate"));
        var activeIndex = db.Model.FindEntityType(typeof(Domain.DeliveryGroups.CustomerDeliveryGroupAssignment))!
            .GetIndexes().Single(i => i.GetDatabaseName() == "UX_CustomerDeliveryGroupAssignments_ActiveCustomer");
        Assert.Equal("\"IsActive\" = TRUE", activeIndex.GetFilter());
    }

    [Theory]
    [InlineData("postgresql://alex:p%40ss%3Aword@ep-cool-1.ap-southeast-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require",
        "ep-cool-1.ap-southeast-1.aws.neon.tech", 5432, "neondb", "alex", "p@ss:word", Npgsql.SslMode.Require)]
    [InlineData("postgres://u:pw@db.example.com:6543/app", "db.example.com", 6543, "app", "u", "pw", Npgsql.SslMode.Prefer)]
    public void Hosted_postgresql_urls_become_npgsql_connection_strings(string url, string host, int port, string database, string user,
        string password, Npgsql.SslMode ssl)
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseProviderConfigurator.ToNpgsqlConnectionString(url));
        Assert.Equal((host, port, database, user, password, ssl), (csb.Host, csb.Port, csb.Database, csb.Username, csb.Password, csb.SslMode));
        Assert.Equal("Host=h;Database=d", DatabaseProviderConfigurator.ToNpgsqlConnectionString("Host=h;Database=d"));
    }

    private static async Task<List<string>> QueryStrings(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }
}
