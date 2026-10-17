using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fitzobit.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// PostgreSQL connection string: Npgsql format ("Host=…;Database=…;Username=…;Password=…") or a postgresql:// URL.
    /// Never stored in the repository: user secrets locally, an environment variable (Database__ConnectionString) when hosted.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Apply pending migrations when the application starts.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}

/// <summary>The single place that configures EF Core for PostgreSQL (Npgsql).</summary>
public static class DatabaseConfigurator
{
    public const string MissingConnectionStringMessage =
        "Database:ConnectionString is not set. Locally: dotnet user-secrets set \"Database:ConnectionString\" " +
        "\"Host=localhost;Port=5432;Database=fitzobit;Username=postgres;Password=…\" --project src/Fitzobit.Web. " +
        "Or put it in src/Fitzobit.Web/appsettings.Local.json (git-ignored). Both are read only when ASPNETCORE_ENVIRONMENT is " +
        "Development (.\\start.ps1 or the \"http\" launch profile). Hosted: set the environment variable Database__ConnectionString. " +
        "See README, \"Local PostgreSQL\".";

    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException(MissingConnectionStringMessage);
        builder.UseNpgsql(ToNpgsqlConnectionString(connectionString), npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory"));
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

    /// <summary>Host and database name only (never user or password), for display.</summary>
    public static string Describe(string connectionString)
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(ToNpgsqlConnectionString(connectionString));
            return $"{csb.Database} on {csb.Host}";
        }
        catch (ArgumentException)
        {
            return "(unreadable connection string)";
        }
    }
}
