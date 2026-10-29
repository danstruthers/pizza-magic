using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PizzaMagic.OrderManager.Data;

namespace PizzaMagic.OrderManager.Tests.Persistence;

/// <summary>
/// Fantasy realm (plan section 7.5): the default and fantasy databases live side by side; <c>--database</c>
/// (Seed:Profile) picks one, defaulting to the normal database, and neither profile ever applies its scripts to the
/// other's file.
/// </summary>
public class DatabaseProfileTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "pizzamagic-profile-folder");

    private static IConfiguration CommandLine(params string[] args) =>
        new ConfigurationBuilder().AddCommandLine(args, DatabaseSelection.SwitchMappings).Build();

    private static IConfiguration Settings(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value))).Build();

    private static string DataSource(DatabaseSelection selection) => new SqliteConnectionStringBuilder(selection.ConnectionString).DataSource;

    [Fact]
    public void No_option_selects_the_default_database()
    {
        var selection = DatabaseSelection.Resolve(CommandLine(), Folder);

        selection.Profile.ShouldBe(SeedProfile.Default);
        DataSource(selection).ShouldBe(Path.Combine(Folder, "pizzamagic.db"));
        selection.IsExplicitConnectionString.ShouldBeFalse();
        DatabaseSelection.ProfileScripts(SeedProfile.Default).ShouldBeEmpty();
    }

    [Fact]
    public void Database_fantasy_selects_a_separate_file_in_the_same_folder()
    {
        var selection = DatabaseSelection.Resolve(CommandLine("--database", "fantasy"), Folder);

        selection.Profile.ShouldBe(SeedProfile.Fantasy);
        DataSource(selection).ShouldBe(Path.Combine(Folder, "pizzamagic-fantasy.db"));
        DatabaseSelection.ProfileScripts(SeedProfile.Fantasy).ShouldBe(["test/fantasy-realm.sql"]);
    }

    [Theory]
    [InlineData("--database", "default", SeedProfile.Default)]
    [InlineData("--database", "Default", SeedProfile.Default)]
    [InlineData("--database", "FANTASY", SeedProfile.Fantasy)]
    [InlineData("--database=fantasy", null, SeedProfile.Fantasy)]
    [InlineData("--Seed:Profile=Fantasy", null, SeedProfile.Fantasy)]
    public void Option_spellings_select_the_profile(string arg, string? value, SeedProfile expected)
    {
        var args = value is null ? new[] { arg } : [arg, value];

        DatabaseSelection.Resolve(CommandLine(args), Folder).Profile.ShouldBe(expected);
    }

    [Fact]
    public void Environment_style_setting_selects_the_profile()
    {
        DatabaseSelection.Resolve(Settings(("Seed:Profile", "Fantasy")), Folder).Profile.ShouldBe(SeedProfile.Fantasy);
    }

    [Theory]
    [InlineData("dragons")]
    [InlineData("fantasy.db")]
    [InlineData(" ")]
    public void Unknown_database_fails_fast_with_a_clear_message(string value)
    {
        var error = Should.Throw<DatabaseProfileException>(() => DatabaseSelection.Resolve(CommandLine("--database", value), Folder));

        error.Message.ShouldContain($"'{value}'");
        error.Message.ShouldContain("--database default");
        error.Message.ShouldContain("--database fantasy");
    }

    // Decision: an explicit connection string wins over the profile's default file (tests and deployments set one);
    // the profile still picks the scripts, and the data_script_log guard keeps the two datasets apart.
    [Fact]
    public void Explicit_connection_string_wins_over_the_profile_file()
    {
        var configuration = Settings(("ConnectionStrings:PizzaMagic", "Data Source=elsewhere.db"), ("Seed:Profile", "Fantasy"));

        var selection = DatabaseSelection.Resolve(configuration, Folder);

        selection.ShouldBe(new DatabaseSelection(SeedProfile.Fantasy, "Data Source=elsewhere.db", IsExplicitConnectionString: true));
    }

    [Fact]
    public async Task Default_profile_database_has_no_fantasy_rows()
    {
        await using var database = await TestDatabase.CreateSeededAsync(SeedProfile.Default);

        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM restaurant")).ShouldBe(1);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM time_zone WHERE iana_id LIKE 'Realm/%'")).ShouldBe(0);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM data_script_log WHERE name LIKE 'test/%'")).ShouldBe(0);
    }

    [Fact]
    public async Task Fantasy_profile_applies_the_core_seeds_then_the_realm()
    {
        await using var database = await TestDatabase.CreateSeededAsync(SeedProfile.Fantasy);
        await using var db = database.NewContext();

        db.DataScriptLog.OrderBy(l => l.Name).Select(l => l.Name).ToList()
            .ShouldBe(["seed/0000-time-zones.sql", "seed/0001-core.sql", "test/fantasy-realm.sql"]);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM restaurant")).ShouldBe(7);
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM restaurant WHERE is_default = 1 AND code = 'MAIN'")).ShouldBe(1);
    }

    [Fact]
    public async Task Restarting_on_the_fantasy_database_is_a_no_op()
    {
        await using var database = await TestDatabase.CreateSeededAsync(SeedProfile.Fantasy);

        await database.SeedAsync(SeedProfile.Fantasy);

        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM data_script_log")).ShouldBe(3);
    }

    [Fact]
    public async Task Default_profile_refuses_a_fantasy_database()
    {
        await using var database = await TestDatabase.CreateSeededAsync(SeedProfile.Fantasy);

        var error = await Should.ThrowAsync<DatabaseProfileException>(() => database.SeedAsync(SeedProfile.Default));

        error.Message.ShouldContain("fantasy");
    }

    [Fact]
    public async Task Fantasy_profile_refuses_a_default_database()
    {
        await using var database = await TestDatabase.CreateSeededAsync(SeedProfile.Default);

        var error = await Should.ThrowAsync<DatabaseProfileException>(() => database.SeedAsync(SeedProfile.Fantasy));

        error.Message.ShouldContain("default");
        (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM restaurant")).ShouldBe(1);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.NotFound)]
    [InlineData("Default", HttpStatusCode.NotFound)]
    [InlineData("Fantasy", HttpStatusCode.OK)]
    public async Task Service_serves_realm_restaurants_only_on_the_fantasy_profile(string? profile, HttpStatusCode sunspireMenu)
    {
        await using var factory = new ProfileFactory(profile);
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/menu")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/menu?restaurantId=1003")).StatusCode.ShouldBe(sunspireMenu);
    }

    // The real service, as a process: a bad --database stops startup with one clear line and exit code 2, not a stack
    // trace; a valueless --database is refused too (the command-line provider would otherwise drop it silently and
    // open the default database).
    [Theory]
    [InlineData("Unknown database 'dragons'", "--database", "dragons")]
    [InlineData("--database needs a value: default or fantasy.", "--database")]
    [InlineData("--database needs a value: default or fantasy.", "--database", "--urls", "http://localhost:5199")]
    public async Task Service_refuses_to_start_with_a_bad_database_option(string message, params string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pizzamagic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "PizzaMagic.OrderManager.dll"));
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        using var process = System.Diagnostics.Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(TimeSpan.FromSeconds(30));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        exited.ShouldBeTrue("the service started instead of refusing the option");
        process.ExitCode.ShouldBe(2);
        var error = await stderr;
        error.ShouldContain(message);
        error.ShouldNotContain("   at ");   // no stack trace
        File.Exists(Path.Combine(directory, "pizzamagic.db")).ShouldBeFalse();
    }

    /// <summary>The real service with a temp-file connection string from configuration and the given profile.</summary>
    private sealed class ProfileFactory(string? profile) : WebApplicationFactory<OrderManagerMarker>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "pizzamagic-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_directory);
            builder.UseSetting("ConnectionStrings:PizzaMagic", $"Data Source={Path.Combine(_directory, "profile.db")};Default Timeout=5;Pooling=False");
            if (profile is not null)
            {
                builder.UseSetting(DatabaseSelection.ProfileKey, profile);
            }
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestDatabase.DefaultNow));
            });
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
}
