using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fitzobit.Infrastructure.Persistence;

/// <summary>
/// SQLite-only connection settings, applied whenever a connection opens:
/// WAL journaling by default (readers are not blocked while an import is being saved) with NORMAL sync (the recommended
/// pairing with WAL), and a 64 MB page cache so large imports do not spill to disk mid-transaction
/// (10,000 staged rows: about 20 s with the default cache, under 2 s with this one).
/// On network-backed storage (e.g. Azure App Service's D:\home) WAL is unsafe, so the journal mode is configurable;
/// any other mode uses FULL sync.
/// </summary>
public sealed class SqliteConnectionTuning(string journalMode = SqliteConnectionTuning.DefaultJournalMode) : DbConnectionInterceptor
{
    public const string DefaultJournalMode = "WAL";
    private static readonly string[] AllowedJournalModes = ["WAL", "DELETE", "TRUNCATE", "PERSIST"];

    private readonly string _pragmas = BuildPragmas(journalMode);

    private static string BuildPragmas(string journalMode)
    {
        var mode = (journalMode ?? DefaultJournalMode).Trim().ToUpperInvariant();
        if (!AllowedJournalModes.Contains(mode))
            throw new ArgumentException($"Database:SqliteJournalMode must be one of {string.Join(", ", AllowedJournalModes)}.", nameof(journalMode));
        var sync = mode == "WAL" ? "NORMAL" : "FULL";
        return $"PRAGMA journal_mode={mode}; PRAGMA synchronous={sync}; PRAGMA cache_size=-65536; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;";
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = _pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
