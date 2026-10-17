using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Persistence;

/// <summary>Brings the database up to date at startup by applying pending migrations (which also seed the drop-down values).</summary>
public sealed class DatabaseInitializer(FitzobitDbContext db, IOptions<DatabaseOptions> databaseOptions, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!databaseOptions.Value.MigrateOnStartup) return;

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            logger.LogInformation("Applying {Count} migration(s) to {Database}: {Migrations}", pending.Count,
                DatabaseConfigurator.Describe(databaseOptions.Value.ConnectionString), string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
