using Fitzobit.Application.Abstractions;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fitzobit.Infrastructure.PostgreSql;

/// <summary>
/// Used by "dotnet ef" only, to create PostgreSQL migrations in this assembly. Creating a migration needs no database;
/// applying one ("dotnet ef database update") uses FITZOBIT_PG_CONNECTION. The app applies migrations itself at startup.
/// </summary>
public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FitzobitDbContext>
{
    public FitzobitDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProviders.PostgreSql,
            ConnectionString = Environment.GetEnvironmentVariable("FITZOBIT_PG_CONNECTION")
                               ?? "Host=localhost;Database=fitzobit_design;Username=postgres"
        };
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseProviderConfigurator.Configure(builder, options, Directory.GetCurrentDirectory());
        return new FitzobitDbContext(builder.Options, new DesignTimeUser(), new SystemClock());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public string UserName => "design-time";
        public string DisplayName => "Design time";
        public string Role => "System";
    }
}
