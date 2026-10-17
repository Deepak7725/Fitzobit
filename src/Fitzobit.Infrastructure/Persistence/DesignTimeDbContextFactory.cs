using Fitzobit.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fitzobit.Infrastructure.Persistence;

/// <summary>
/// Used by "dotnet ef" only. Creating a migration needs no database; applying one from the command line
/// ("dotnet ef database update") uses FITZOBIT_DB_CONNECTION. The app applies migrations itself at startup.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FitzobitDbContext>
{
    public FitzobitDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseConfigurator.Configure(builder,
            Environment.GetEnvironmentVariable("FITZOBIT_DB_CONNECTION") ?? "Host=localhost;Database=fitzobit_design;Username=postgres");
        return new FitzobitDbContext(builder.Options, new DesignTimeUser(), new SystemClock());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public string UserName => "design-time";
        public string DisplayName => "Design time";
        public string Role => "System";
    }
}
