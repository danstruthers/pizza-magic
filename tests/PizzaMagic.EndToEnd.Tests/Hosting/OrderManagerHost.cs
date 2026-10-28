using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PizzaMagic.OrderManager;
using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.EndToEnd.Tests.Hosting;

/// <summary>
/// The real order service in-process: its own temp-file SQLite database, real startup (migrate, seed, validate) and a
/// fake clock at the plan's default instant, Tuesday 2026-10-27 12:00 PDT.
/// </summary>
public sealed class OrderManagerHost : WebApplicationFactory<OrderManagerMarker>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pizzamagic-e2e", Guid.NewGuid().ToString("N"));

    public OrderManagerHost() => Directory.CreateDirectory(_directory);

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.Zero));

    private string ConnectionString => $"Data Source={Path.Combine(_directory, "e2e.db")};Default Timeout=5;Pooling=False";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PizzaMagicDbContext>>();
            services.AddDbContext<PizzaMagicDbContext>(options => options.UsePizzaMagicSqlite(ConnectionString));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>How many orders the database holds: the ground truth for "created exactly once".</summary>
    public async Task<long> OrderCountAsync()
    {
        _ = Server;   // the schema exists once the host has started
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM customer_order";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
