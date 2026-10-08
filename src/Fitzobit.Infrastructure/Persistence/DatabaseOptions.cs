using Microsoft.Data.Sqlite;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" (default, local file) or "PostgreSQL" (hosted, e.g. Neon).</summary>
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    public string ConnectionString { get; set; } = "Data Source=data/fitzobit.db";

    /// <summary>Apply pending migrations when the application starts.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>SQLite journal mode: WAL (default, local disks) or DELETE/TRUNCATE/PERSIST (network storage such as Azure App Service).</summary>
    public string SqliteJournalMode { get; set; } = SqliteConnectionTuning.DefaultJournalMode;
}

public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSQL";
}

/// <summary>
/// The single place that knows which EF Core provider is in use. Adding PostgreSQL later means adding a
/// case here (UseNpgsql + its own migrations assembly/folder); application code stays unchanged.
/// </summary>
public static class DatabaseProviderConfigurator
{
    public const string PostgreSqlMigrationsAssembly = "Fitzobit.Infrastructure.PostgreSql";

    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions options, string basePath)
    {
        switch (options.Provider)
        {
            case var p when p.Equals(DatabaseProviders.Sqlite, StringComparison.OrdinalIgnoreCase):
                builder.UseSqlite(ResolveSqliteConnectionString(options.ConnectionString, basePath),
                        sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory"))
                    .AddInterceptors(new SqliteConnectionTuning(options.SqliteJournalMode));
                break;

            case var p when p.Equals(DatabaseProviders.PostgreSql, StringComparison.OrdinalIgnoreCase):
                // Migrations are provider-specific; PostgreSQL's live in their own assembly.
                builder.UseNpgsql(ToNpgsqlConnectionString(options.ConnectionString),
                    npgsql => npgsql.MigrationsAssembly(PostgreSqlMigrationsAssembly).MigrationsHistoryTable("__EFMigrationsHistory"));
                break;

            default:
                throw new NotSupportedException($"Unknown database provider \"{options.Provider}\".");
        }
    }

    /// <summary>
    /// Makes a relative "Data Source" absolute (relative to <paramref name="basePath"/>) and creates its folder,
    /// so the database location does not depend on the process working directory.
    /// </summary>
    public static string ResolveSqliteConnectionString(string connectionString, string basePath)
    {
        var csb = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(csb.DataSource) || csb.DataSource == ":memory:" || csb.Mode == SqliteOpenMode.Memory)
            return csb.ToString();

        if (!Path.IsPathRooted(csb.DataSource))
            csb.DataSource = Path.GetFullPath(Path.Combine(basePath, csb.DataSource));

        Directory.CreateDirectory(Path.GetDirectoryName(csb.DataSource)!);
        return csb.ToString();
    }

    /// <summary>
    /// Accepts both Npgsql "Host=…;Username=…" strings and the postgresql:// URLs that hosted providers (Neon, Render) show.
    /// </summary>
    public static string ToNpgsqlConnectionString(string connectionString)
    {
        var value = (connectionString ?? "").Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        var user = uri.UserInfo.Split(':', 2);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null
        };
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(kv[1].Replace("-", ""), ignoreCase: true, out var mode))
                csb.SslMode = mode;
        }
        return csb.ConnectionString;
    }

    public static string? GetSqliteFilePath(string connectionString, string basePath)
    {
        var csb = new SqliteConnectionStringBuilder(ResolveSqliteConnectionString(connectionString, basePath));
        return csb.DataSource == ":memory:" ? null : csb.DataSource;
    }
}
