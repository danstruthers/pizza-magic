using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Tests.Persistence;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>
/// The real order service in-process, on its own temp-file SQLite database and a fake clock (plan default:
/// Tuesday 2026-10-27 12:00 PDT). Startup runs for real: migrate, seed, validate.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<OrderManagerMarker>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pizzamagic-tests", Guid.NewGuid().ToString("N"));

    public ApiFactory() => Directory.CreateDirectory(_directory);

    public FakeTimeProvider Clock { get; } = new(TestDatabase.DefaultNow);

    public string ConnectionString => $"Data Source={Path.Combine(_directory, "api.db")};Default Timeout=5;Pooling=False";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory);
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PizzaMagicDbContext>>();
            services.AddDbContext<PizzaMagicDbContext>(options => options.UsePizzaMagicSqlite(ConnectionString));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

public static class HttpTestExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The validation problem's errors as (path, code) pairs, in document order.</summary>
    public static IEnumerable<(string Path, string Code)> FieldErrors(this JsonElement problem) =>
        problem.GetProperty("errors").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateArray().Select(e => (p.Name, e.GetProperty("code").GetString()!)));
}
