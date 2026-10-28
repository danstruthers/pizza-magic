using Microsoft.EntityFrameworkCore;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Data;

/// <summary>
/// The order service's database. The schema is the hand-written SQL in Data/Schema (plan section 4.3), applied by
/// the InitialSchema migration; this model only maps the tables the code reads and writes, by explicit table name.
/// </summary>
public sealed class PizzaMagicDbContext(DbContextOptions<PizzaMagicDbContext> options) : DbContext(options)
{
    public DbSet<TaxClassEntity> TaxClasses => Set<TaxClassEntity>();
    public DbSet<TaxJurisdictionEntity> TaxJurisdictions => Set<TaxJurisdictionEntity>();
    public DbSet<TimeZoneEntity> TimeZones => Set<TimeZoneEntity>();
    public DbSet<TimeZoneOffsetPeriodEntity> TimeZoneOffsetPeriods => Set<TimeZoneOffsetPeriodEntity>();
    public DbSet<RestaurantEntity> Restaurants => Set<RestaurantEntity>();
    public DbSet<OrderingPolicyEntity> OrderingPolicies => Set<OrderingPolicyEntity>();
    public DbSet<ProductCategoryEntity> ProductCategories => Set<ProductCategoryEntity>();
    public DbSet<ToppingTierEntity> ToppingTiers => Set<ToppingTierEntity>();
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<PortionTypeEntity> PortionTypes => Set<PortionTypeEntity>();
    public DbSet<RestaurantProductOverrideEntity> RestaurantProductOverrides => Set<RestaurantProductOverrideEntity>();
    public DbSet<PromotionEntity> Promotions => Set<PromotionEntity>();
    public DbSet<PromotionCodeEntity> PromotionCodes => Set<PromotionCodeEntity>();
    public DbSet<PromotionConditionEntity> PromotionConditions => Set<PromotionConditionEntity>();
    public DbSet<PromotionIncompatibilityEntity> PromotionIncompatibilities => Set<PromotionIncompatibilityEntity>();
    public DbSet<PromotionRestaurantEntity> PromotionRestaurants => Set<PromotionRestaurantEntity>();
    public DbSet<DataScriptLogEntity> DataScriptLog => Set<DataScriptLogEntity>();
    public DbSet<CustomerOrderEntity> Orders => Set<CustomerOrderEntity>();
    public DbSet<OrderLineEntity> OrderLines => Set<OrderLineEntity>();
    public DbSet<OrderPromotionEntity> OrderPromotions => Set<OrderPromotionEntity>();
    public DbSet<OrderTaxLineEntity> OrderTaxLines => Set<OrderTaxLineEntity>();
    public DbSet<OrderStatusHistoryEntity> OrderStatusHistory => Set<OrderStatusHistoryEntity>();
    public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TaxClassEntity>().ToTable("tax_class");
        model.Entity<TaxJurisdictionEntity>().ToTable("tax_jurisdiction");
        model.Entity<TimeZoneEntity>().ToTable("time_zone");
        model.Entity<TimeZoneOffsetPeriodEntity>().ToTable("time_zone_offset_period");
        model.Entity<RestaurantEntity>().ToTable("restaurant");
        model.Entity<OrderingPolicyEntity>().ToTable("ordering_policy").HasKey(p => p.RestaurantId);
        model.Entity<ProductCategoryEntity>().ToTable("product_category");
        model.Entity<ToppingTierEntity>().ToTable("topping_tier");
        model.Entity<ProductEntity>().ToTable("product");
        model.Entity<PortionTypeEntity>().ToTable("portion_type").HasKey(p => p.Code);
        model.Entity<RestaurantProductOverrideEntity>().ToTable("restaurant_product_override").HasKey(o => new { o.RestaurantId, o.ProductId });
        model.Entity<PromotionEntity>().ToTable("promotion");
        model.Entity<PromotionCodeEntity>().ToTable("promotion_code").HasKey(c => c.Code);
        model.Entity<PromotionConditionEntity>().ToTable("promotion_condition");
        model.Entity<PromotionIncompatibilityEntity>().ToTable("promotion_incompatibility").HasKey(p => new { p.PromotionIdLow, p.PromotionIdHigh });
        model.Entity<PromotionRestaurantEntity>().ToTable("promotion_restaurant").HasKey(p => new { p.PromotionId, p.RestaurantId });
        model.Entity<DataScriptLogEntity>().ToTable("data_script_log").HasKey(l => l.Name);

        // Orders. No EF concurrency token: versions are bumped by explicit conditional UPDATEs (section 2.10).
        model.Entity<CustomerOrderEntity>(order =>
        {
            order.ToTable("customer_order");
            order.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
            order.HasMany(o => o.Promotions).WithOne().HasForeignKey(p => p.OrderId);
            order.HasMany(o => o.TaxLines).WithOne().HasForeignKey(t => t.OrderId);
            order.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId);
        });
        model.Entity<OrderLineEntity>(line =>
        {
            line.ToTable("order_line");
            line.HasMany(l => l.Toppings).WithOne().HasForeignKey(t => t.OrderLineId);
        });
        model.Entity<OrderLineToppingEntity>().ToTable("order_line_topping");
        model.Entity<OrderPromotionEntity>(promotion =>
        {
            promotion.ToTable("order_promotion");
            promotion.HasMany(p => p.Allocations).WithOne().HasForeignKey(a => a.OrderPromotionId);
        });
        model.Entity<OrderDiscountAllocationEntity>(allocation =>
        {
            allocation.ToTable("order_discount_allocation");
            allocation.HasOne(a => a.OrderLine).WithMany().HasForeignKey(a => a.OrderLineId);
            allocation.HasOne(a => a.OrderLineTopping).WithMany().HasForeignKey(a => a.OrderLineToppingId);
        });
        model.Entity<OrderTaxLineEntity>().ToTable("order_tax_line");
        model.Entity<OrderStatusHistoryEntity>().ToTable("order_status_history");
        model.Entity<IdempotencyRecordEntity>().ToTable("idempotency_record").HasKey(r => new { r.IdempotencyKey, r.Operation });

        ApplyTimeConverters(model);
    }

    /// <summary>
    /// A72: every DateTime is either a UTC instant (property name ends in Utc) or a restaurant-local business value
    /// (ends in Local). Anything else is a modelling error, so it fails here rather than being stored ambiguously.
    /// </summary>
    private static void ApplyTimeConverters(ModelBuilder model)
    {
        foreach (var property in model.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.ClrType != typeof(DateTime) && property.ClrType != typeof(DateTime?))
            {
                continue;
            }
            if (property.Name.EndsWith("Utc", StringComparison.Ordinal))
            {
                property.SetValueConverter(new UtcTimestampConverter());
            }
            else if (property.Name.EndsWith("Local", StringComparison.Ordinal))
            {
                property.SetValueConverter(new LocalWallClockConverter());
            }
            else
            {
                throw new InvalidOperationException($"{property.DeclaringType.ClrType.Name}.{property.Name} must end in Utc or Local (A72).");
            }
        }
    }
}

public static class DbContextOptionsExtensions
{
    public static DbContextOptionsBuilder<PizzaMagicDbContext> UsePizzaMagicSqlite(
        this DbContextOptionsBuilder<PizzaMagicDbContext> builder, string connectionString) =>
        builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention();

    public static DbContextOptionsBuilder UsePizzaMagicSqlite(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention();
}
