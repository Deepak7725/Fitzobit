using Fitzobit.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fitzobit.Infrastructure.Persistence;

/// <summary>
/// Used by "dotnet ef" only. Points at the repository's data\fitzobit.db (located by walking up to Fitzobit.sln),
/// or at FITZOBIT_DB_CONNECTION when set.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FitzobitDbContext>
{
    public FitzobitDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("FITZOBIT_DB_CONNECTION") ?? "Data Source=data/fitzobit.db"
        };

        var builder = new DbContextOptionsBuilder<FitzobitDbContext>();
        DatabaseProviderConfigurator.Configure(builder, options, FindRepositoryRoot());
        return new FitzobitDbContext(builder.Options, new DesignTimeUser(), new SystemClock());
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Fitzobit.sln"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public string UserName => "design-time";
        public string DisplayName => "Design time";
        public string Role => "System";
    }
}
