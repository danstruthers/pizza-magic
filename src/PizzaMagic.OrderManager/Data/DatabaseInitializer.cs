using Microsoft.EntityFrameworkCore;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Promotions;

namespace PizzaMagic.OrderManager.Data;

/// <summary>The catalog failed validation at startup: the service refuses to start (fail fast).</summary>
public sealed class CatalogValidationException(IReadOnlyList<string> errors)
    : Exception("The catalog is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Startup (plan section 4.6): migrate (schema only), switch to WAL, apply the seed scripts, then validate the
/// catalog and refuse to start if it is invalid.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(PizzaMagicDbContext db, string dataDirectory, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        // journal_mode cannot change inside a transaction, so it runs on its own.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await DataScriptRunner.ExecuteAsync(db.Database.GetDbConnection(), null, "PRAGMA journal_mode=WAL;", cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        await new DataScriptRunner(db, clock).ApplySeedsAsync(dataDirectory, cancellationToken);
    }

    public static async Task InitializeAsync(
        PizzaMagicDbContext db,
        string dataDirectory,
        TimeProvider clock,
        PromotionRegistry promotions,
        PricingMethodRegistry pricingMethods,
        CancellationToken cancellationToken = default)
    {
        await MigrateAndSeedAsync(db, dataDirectory, clock, cancellationToken);
        var errors = await new CatalogValidator(db, promotions, pricingMethods, clock).ValidateAsync(cancellationToken);
        if (errors.Count > 0)
        {
            throw new CatalogValidationException(errors);
        }
    }
}
