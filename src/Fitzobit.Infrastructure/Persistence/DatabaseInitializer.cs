using Fitzobit.Application.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Persistence;

/// <summary>Brings the database up to date at startup: applies migrations, then syncs the catalogue snapshot.</summary>
public sealed class DatabaseInitializer(
    FitzobitDbContext db,
    CatalogueSeeder catalogueSeeder,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<Catalogue.CatalogueSeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (databaseOptions.Value.MigrateOnStartup)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (seedOptions.Value.Enabled)
            await catalogueSeeder.SeedAsync(cancellationToken);
    }
}
