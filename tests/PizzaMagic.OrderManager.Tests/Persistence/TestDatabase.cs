using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.OrderManager.Tests.Persistence;

/// <summary>
/// A real SQLite database in a temp file, initialized by the same startup path as the service (migrate, WAL,
/// seed scripts, catalog validation). Never the EF InMemory provider (plan section 4.6).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    /// <summary>Plan default clock: Tuesday 2026-10-27 12:00 PDT.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 27, 19, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pizzamagic-tests", Guid.NewGuid().ToString("N"));

    private TestDatabase()
    {
        Directory.CreateDirectory(_directory);
        ConnectionString = $"Data Source={Path.Combine(_directory, "pizzamagic.db")};Default Timeout=5;Pooling=False";
    }

    public string ConnectionString { get; }

    public FakeTimeProvider Clock { get; } = new(DefaultNow);

    /// <summary>The repository's data/ folder as copied next to the test binaries.</summary>
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>A migrated database with the profile's seed scripts applied, but not validated.</summary>
    public static async Task<TestDatabase> CreateSeededAsync(SeedProfile profile = SeedProfile.Default)
    {
        var database = new TestDatabase();
        await database.SeedAsync(profile);
        return database;
    }

    /// <summary>Runs the startup migrate-and-seed step (again) on this database file with the given profile.</summary>
    public async Task SeedAsync(SeedProfile profile)
    {
        await using var db = NewContext();
        await DatabaseInitializer.MigrateAndSeedAsync(db, DataDirectory, Clock, profile);
    }

    /// <summary>An empty database file with no schema.</summary>
    public static TestDatabase CreateEmpty() => new();

    public PizzaMagicDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PizzaMagicDbContext>().UsePizzaMagicSqlite(ConnectionString).Options);

    /// <summary>Runs raw SQL as-is (ADO.NET, not ExecuteSqlRaw, which treats JSON braces as format placeholders).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> ScalarAsync(string sql)
    {
        await using var db = NewContext();
        return await db.Database.SqlQueryRaw<long>(sql).SingleAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a file still locked by the OS is cleaned up with the temp folder.
        }
        await Task.CompletedTask;
    }
}
