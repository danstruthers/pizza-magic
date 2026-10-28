using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;

namespace PizzaMagic.OrderManager.Data;

// M8 red-phase stubs: compile, fail at run time.
#pragma warning disable CS9113 // Stub parameters are unread until the implementation.

public sealed class PizzaMagicDbContext(DbContextOptions<PizzaMagicDbContext> options) : DbContext(options);

public static class DbContextOptionsExtensions
{
    public static DbContextOptionsBuilder<PizzaMagicDbContext> UsePizzaMagicSqlite(this DbContextOptionsBuilder<PizzaMagicDbContext> builder, string connectionString) =>
        throw new NotImplementedException();
}

public static class DatabaseInitializer
{
    public static Task MigrateAndSeedAsync(PizzaMagicDbContext db, string dataDirectory, TimeProvider clock) => throw new NotImplementedException();

    public static Task InitializeAsync(PizzaMagicDbContext db, string dataDirectory, TimeProvider clock, PromotionRegistry promotions, PricingMethodRegistry pricingMethods) =>
        throw new NotImplementedException();
}

public enum DataScriptOutcome { Applied, AlreadyApplied }

public sealed record DataScriptResult(string Name, DataScriptOutcome Outcome);

public sealed class DataScriptChangedException(string message) : Exception(message);

public sealed class DataScriptRunner(PizzaMagicDbContext db, TimeProvider clock)
{
    public Task<IReadOnlyList<DataScriptResult>> ApplySeedsAsync(string dataDirectory) => throw new NotImplementedException();
}

public sealed class CatalogLoader(PizzaMagicDbContext db)
{
    public Task<PricingCatalog> LoadAsync(long? restaurantId) => throw new NotImplementedException();
}

public sealed class CatalogValidationException(IReadOnlyList<string> errors) : Exception("The catalog is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class CatalogValidator(PizzaMagicDbContext db, PromotionRegistry promotions, PricingMethodRegistry pricingMethods, TimeProvider clock)
{
    public Task<IReadOnlyList<string>> ValidateAsync() => throw new NotImplementedException();
}

public sealed class UtcTimestampConverter() : ValueConverter<DateTime, string>(v => Stub.Write(v), s => Stub.Read(s));

public sealed class LocalWallClockConverter() : ValueConverter<DateTime, string>(v => Stub.Write(v), s => Stub.Read(s));

internal static class Stub
{
    public static string Write(DateTime value) => throw new NotImplementedException();

    public static DateTime Read(string value) => throw new NotImplementedException();
}
