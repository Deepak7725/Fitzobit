using Fitzobit.Application.Abstractions;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Diagnostics;

/// <param name="Database">Database name and host only; never the user name or password.</param>
public sealed record SystemInfoDto(
    string ApplicationName,
    string Version,
    string DatabaseProvider,
    string Database,
    long? DatabaseSizeBytes,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> Tables,
    string UserName,
    string UserDisplayName,
    string UserRole);

/// <summary>Read-only runtime facts for the Settings screen.</summary>
public sealed class SystemInfoService(FitzobitDbContext db, IOptions<DatabaseOptions> databaseOptions, ICurrentUser currentUser)
{
    public async Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n != null)
            .Select(n => n!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var size = await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync(cancellationToken);

        return new SystemInfoDto(
            "Fitzobit Delivery Management",
            typeof(SystemInfoService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            "PostgreSQL",
            DatabaseConfigurator.Describe(databaseOptions.Value.ConnectionString),
            size,
            (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList(),
            (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList(),
            tables,
            currentUser.UserName,
            currentUser.DisplayName,
            currentUser.Role);
    }
}
