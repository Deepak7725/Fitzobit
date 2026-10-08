using Fitzobit.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

public class SqliteTuningTests
{
    [Theory]
    [InlineData("WAL", "wal", 1)]
    [InlineData("delete", "delete", 2)]
    public async Task Journal_mode_is_configurable(string configured, string expected, long synchronous)
    {
        var file = Path.Combine(Path.GetTempPath(), $"fitzobit-tuning-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<FitzobitDbContext>();
            DatabaseProviderConfigurator.Configure(options,
                new DatabaseOptions { ConnectionString = $"Data Source={file}", SqliteJournalMode = configured }, Path.GetTempPath());
            await using var db = new FitzobitDbContext(options.Options, new TestUser(), new FixedClock(DateTime.UtcNow));
            await db.Database.OpenConnectionAsync();
            var connection = db.Database.GetDbConnection();

            await using var mode = connection.CreateCommand();
            mode.CommandText = "PRAGMA journal_mode";
            Assert.Equal(expected, (string?)await mode.ExecuteScalarAsync());
            await using var sync = connection.CreateCommand();
            sync.CommandText = "PRAGMA synchronous";
            Assert.Equal(synchronous, (long?)await sync.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { file, file + "-wal", file + "-shm" })
                try { File.Delete(f); } catch (IOException) { }
        }
    }

    [Fact]
    public void Unknown_journal_mode_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new SqliteConnectionTuning("MEMORY"));
}
