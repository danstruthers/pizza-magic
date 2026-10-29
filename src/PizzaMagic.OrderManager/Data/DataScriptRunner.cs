using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace PizzaMagic.OrderManager.Data;

public enum DataScriptOutcome
{
    Applied,
    AlreadyApplied,
}

public sealed record DataScriptResult(string Name, DataScriptOutcome Outcome);

/// <summary>An applied script whose content changed: scripts are append-only, like migrations.</summary>
public sealed class DataScriptChangedException(string message) : Exception(message);

/// <summary>
/// Applies the SQL data scripts under data/ (plan section 2.12). Each script runs once, in its own
/// transaction, and is logged with its SHA-256 in data_script_log. Same name and hash: no-op. Same name, different
/// hash: refused before anything runs.
/// </summary>
public sealed class DataScriptRunner(PizzaMagicDbContext db, TimeProvider clock)
{
    public Task<IReadOnlyList<DataScriptResult>> ApplySeedsAsync(string dataDirectory, CancellationToken cancellationToken = default) =>
        ApplyAsync(dataDirectory, SeedProfile.Default, cancellationToken);

    /// <summary>
    /// Applies data/seed/*.sql in name order, then the profile's own scripts (the fantasy realm's). A database
    /// belongs to one profile: the default profile refuses a database holding the fantasy realm, and the fantasy
    /// profile refuses a database first seeded without it, so neither dataset is ever applied to the other's file.
    /// </summary>
    public async Task<IReadOnlyList<DataScriptResult>> ApplyAsync(string dataDirectory, SeedProfile profile, CancellationToken cancellationToken = default)
    {
        var scripts = Directory.GetFiles(Path.Combine(dataDirectory, "seed"), "*.sql")
            .Order(StringComparer.Ordinal)
            .Select(path => (Name: "seed/" + Path.GetFileName(path), Path: path))
            .Concat(DatabaseSelection.ProfileScripts(profile).Select(name => (Name: name, Path: Path.Combine(dataDirectory, name))))
            .Select(script => (script.Name, script.Path, Hash: Sha256(script.Path)))
            .ToList();

        var applied = await db.DataScriptLog.AsNoTracking().ToDictionaryAsync(l => l.Name, l => l.Sha256, cancellationToken);
        CheckProfile(applied.Keys, profile);
        foreach (var script in scripts)
        {
            if (applied.TryGetValue(script.Name, out var hash) && hash != script.Hash)
            {
                throw new DataScriptChangedException($"{script.Name} was already applied with different content; put the change in a new file.");
            }
        }

        var results = new List<DataScriptResult>();
        foreach (var script in scripts)
        {
            if (applied.ContainsKey(script.Name))
            {
                results.Add(new DataScriptResult(script.Name, DataScriptOutcome.AlreadyApplied));
                continue;
            }
            await ApplyAsync(script.Name, await File.ReadAllTextAsync(script.Path, cancellationToken), script.Hash, cancellationToken);
            results.Add(new DataScriptResult(script.Name, DataScriptOutcome.Applied));
        }
        return results;
    }

    private static void CheckProfile(IReadOnlyCollection<string> applied, SeedProfile profile)
    {
        var holdsFantasy = applied.Contains(DatabaseSelection.FantasyScript);
        if (profile == SeedProfile.Default && holdsFantasy)
        {
            throw new DatabaseProfileException(
                "This database holds the fantasy realm test data. Start with --database fantasy, or point ConnectionStrings:PizzaMagic at another file.");
        }
        if (profile == SeedProfile.Fantasy && applied.Count > 0 && !holdsFantasy)
        {
            throw new DatabaseProfileException(
                "This database was created by the default profile; the fantasy realm needs its own file. Start without --database fantasy, " +
                "or point ConnectionStrings:PizzaMagic at a new file.");
        }
    }

    private async Task ApplyAsync(string name, string sql, string hash, CancellationToken cancellationToken)
    {
        // Raw ADO.NET, not ExecuteSqlRaw: scripts contain JSON braces, which EF would treat as format placeholders.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, sql, cancellationToken);

            await using var log = connection.CreateCommand();
            log.Transaction = transaction;
            log.CommandText = "INSERT INTO data_script_log (name, sha256, applied_at_utc) VALUES ($name, $hash, $at)";
            AddParameter(log, "$name", name);
            AddParameter(log, "$hash", hash);
            AddParameter(log, "$at", UtcTimestampConverter.Write(clock.GetUtcNow().UtcDateTime));
            await log.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    internal static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
