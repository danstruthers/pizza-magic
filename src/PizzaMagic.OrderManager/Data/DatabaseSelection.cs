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
/// The database the order service runs on. The profile comes from <c>Seed:Profile</c>, which the
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

    /// <summary>
    /// The configuration provider silently drops a switch with no value (<c>--database</c> alone, or followed by another
    /// option), which would start the default database; refuse that instead.
    /// </summary>
    public static void CheckArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals("--database", StringComparison.OrdinalIgnoreCase)
                && (i + 1 == args.Count || args[i + 1].StartsWith('-')))
            {
                throw new DatabaseProfileException("--database needs a value: default or fantasy.");
            }
        }
    }

    /// <summary>No value means the default database; anything other than default/fantasy (any case) is refused.</summary>
    public static SeedProfile ParseProfile(string? value) => value?.ToUpperInvariant() switch
    {
        null => SeedProfile.Default,
        "DEFAULT" => SeedProfile.Default,
        "FANTASY" => SeedProfile.Fantasy,
        _ => throw new DatabaseProfileException(
            $"Unknown database '{value}'. Use --database default (pizzamagic.db, the normal data) or --database fantasy " +
            "(pizzamagic-fantasy.db, the fantasy realm test data)."),
    };

    public static string FileName(SeedProfile profile) => profile switch
    {
        SeedProfile.Default => "pizzamagic.db",
        SeedProfile.Fantasy => "pizzamagic-fantasy.db",
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    /// <summary>The scripts a profile applies after data/seed/*.sql, relative to data/.</summary>
    public static IReadOnlyList<string> ProfileScripts(SeedProfile profile) => profile switch
    {
        SeedProfile.Default => [],
        SeedProfile.Fantasy => [FantasyScript],
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    /// <summary>Resolves the profile and the connection string; the default file is in <paramref name="directory"/>.</summary>
    public static DatabaseSelection Resolve(IConfiguration configuration, string directory)
    {
        var profile = ParseProfile(configuration[ProfileKey]);
        var configured = configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(configured)
            ? new DatabaseSelection(profile, $"Data Source={Path.Combine(directory, FileName(profile))};Default Timeout=5", IsExplicitConnectionString: false)
            : new DatabaseSelection(profile, configured, IsExplicitConnectionString: true);
    }
}
