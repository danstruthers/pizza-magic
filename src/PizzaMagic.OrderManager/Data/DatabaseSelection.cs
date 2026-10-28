namespace PizzaMagic.OrderManager.Data;

/// <summary>Which seed data a database holds (plan section 7.5): the normal data, or the fantasy realm test data.</summary>
public enum SeedProfile
{
    Default,
    Fantasy,
}

/// <summary>The database option is invalid, or the database file holds the other profile's data.</summary>
public sealed class DatabaseProfileException(string message) : Exception(message);

/// <summary>
/// The database the order service runs on (M15F). The profile comes from <c>Seed:Profile</c>, which the
/// <c>--database</c> command-line switch maps to; it picks the seed scripts and the default file, so the two
/// databases live side by side and never touch each other. An explicit <c>ConnectionStrings:PizzaMagic</c> wins
/// over the default file, but the profile still picks the scripts.
/// </summary>
public sealed record DatabaseSelection(SeedProfile Profile, string ConnectionString, bool IsExplicitConnectionString)
{
    public const string ProfileKey = "Seed:Profile";
    public const string ConnectionStringName = "PizzaMagic";

    /// <summary>The fantasy realm script, relative to data/ (its name in data_script_log).</summary>
    public const string FantasyScript = "test/fantasy-realm.sql";

    /// <summary><c>--database fantasy</c> is <c>Seed:Profile=fantasy</c>.</summary>
    public static IDictionary<string, string> SwitchMappings => new Dictionary<string, string> { ["--database"] = ProfileKey };

    public static SeedProfile ParseProfile(string? value) => throw new NotImplementedException();

    public static string FileName(SeedProfile profile) => throw new NotImplementedException();

    /// <summary>The scripts a profile applies after data/seed/*.sql, relative to data/.</summary>
    public static IReadOnlyList<string> ProfileScripts(SeedProfile profile) => throw new NotImplementedException();

    /// <summary>Resolves the profile and the connection string; the default file is in <paramref name="directory"/>.</summary>
    public static DatabaseSelection Resolve(IConfiguration configuration, string directory) => throw new NotImplementedException();
}
