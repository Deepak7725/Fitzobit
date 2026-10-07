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
    public void PostgreSql_is_reserved_but_not_enabled_in_version_1()
    {
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        var options = new DatabaseOptions { Provider = DatabaseProviders.PostgreSql, ConnectionString = "Host=localhost" };
        Assert.Throws<NotSupportedException>(() => DatabaseProviderConfigurator.Configure(builder, options, Path.GetTempPath()));
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
