using Fitzobit.Application.Abstractions;
using Fitzobit.Infrastructure.Catalogue;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Diagnostics;

public sealed record SystemInfoDto(
    string ApplicationName,
    string Version,
    string DatabaseProvider,
    string? DatabaseFile,
    long? DatabaseSizeBytes,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> Tables,
    string CatalogueSourceFile,
    bool CatalogueSourceExists,
    string UserName,
    string UserDisplayName,
    string UserRole);

/// <summary>Read-only runtime facts for the Settings screen.</summary>
public sealed class SystemInfoService(
    FitzobitDbContext db,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<CatalogueSeedOptions> seedOptions,
    ICurrentUser currentUser)
{
    public async Task<SystemInfoDto> GetAsync(string basePath, CancellationToken cancellationToken = default)
    {
        var options = databaseOptions.Value;
        var dbFile = options.Provider.Equals(DatabaseProviders.Sqlite, StringComparison.OrdinalIgnoreCase)
            ? DatabaseProviderConfigurator.GetSqliteFilePath(options.ConnectionString, basePath)
            : null;

        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n != null)
            .Select(n => n!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var cataloguePath = seedOptions.Value.ResolveFilePath();
        return new SystemInfoDto(
            "Fitzobit Delivery Management",
            typeof(SystemInfoService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            options.Provider,
            dbFile,
            dbFile != null && File.Exists(dbFile) ? new FileInfo(dbFile).Length + (File.Exists(dbFile + "-wal") ? new FileInfo(dbFile + "-wal").Length : 0) : null,
            (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList(),
            (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList(),
            tables,
            cataloguePath,
            File.Exists(cataloguePath),
            currentUser.UserName,
            currentUser.DisplayName,
            currentUser.Role);
    }
}
