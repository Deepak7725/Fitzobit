using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" (version 1). "PostgreSQL" is reserved for a later release.</summary>
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    public string ConnectionString { get; set; } = "Data Source=data/fitzobit.db";

    /// <summary>Apply pending migrations when the application starts.</summary>
    public bool MigrateOnStartup { get; set; } = true;
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
    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions options, string basePath)
    {
        switch (options.Provider)
        {
            case var p when p.Equals(DatabaseProviders.Sqlite, StringComparison.OrdinalIgnoreCase):
                builder.UseSqlite(ResolveSqliteConnectionString(options.ConnectionString, basePath),
                        sqlite => sqlite.MigrationsHistoryTable("__EFMigrationsHistory"))
                    .AddInterceptors(new SqliteConnectionTuning());
                break;

            case var p when p.Equals(DatabaseProviders.PostgreSql, StringComparison.OrdinalIgnoreCase):
                throw new NotSupportedException(
                    "PostgreSQL is not enabled in this version. Add the Npgsql EF Core provider and PostgreSQL migrations, then handle it here.");

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

    public static string? GetSqliteFilePath(string connectionString, string basePath)
    {
        var csb = new SqliteConnectionStringBuilder(ResolveSqliteConnectionString(connectionString, basePath));
        return csb.DataSource == ":memory:" ? null : csb.DataSource;
    }
}
