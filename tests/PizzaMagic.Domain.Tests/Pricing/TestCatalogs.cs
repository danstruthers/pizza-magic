using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.Domain.Tests.Catalog;
using PizzaMagic.Domain.Tests.Promotions;
using PizzaMagic.Domain.Tests.Time;

namespace PizzaMagic.Domain.Tests.Pricing;

/// <summary>
/// The spec's pricing catalog for engine tests: the seed menu, ILOVEPIZZA/SAVE5, WA 6.5% HALF_UP + COMBINED,
/// and the seed ordering policy (20 lines, qty 20, 12 toppings, 3 codes, $500 / 100% tip caps).
/// </summary>
public static class TestCatalogs
{
    public static readonly OrderingPolicy SeedPolicy = new(
        MaxLines: 20, MaxLineQuantity: 20, MaxToppingsPerPizza: 12, MaxPromoCodes: 3, MaxTipAmountCents: 50_000, MaxTipPercentPpm: 1_000_000);

    public static readonly JurisdictionNode Wa =
        new(1, "WA", "Washington State", ParentId: null, 65_000, TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined);

    public static PricingCatalog Spec(
        CatalogSnapshot? snapshot = null,
        RestaurantOverrides? overrides = null,
        PromotionCatalog? promotions = null,
        OrderingPolicy? policy = null) =>
        new(
            EffectiveMenuBuilder.Build(snapshot ?? SeedCatalog.Snapshot(), overrides ?? RestaurantOverrides.None(1), PricingMethodRegistry.Default),
            promotions ?? PromotionFixtures.Seed(),
            [Wa],
            policy ?? SeedPolicy,
            UsTimeZones.LosAngeles());

    public static PricingContext Now => new(PromotionFixtures.DefaultNow);

    public static PricingEngine Engine => new(new PromotionEngine(PromotionRegistry.Default, new RejectLaterConflicts()));

    public static PizzaDraftLine Pizza(string size, int quantity = 1, params string[] toppings) =>
        new(size, quantity, toppings.Select(t => new DraftTopping(t)).ToList());

    public static OrderDraft Draft(IEnumerable<PizzaDraftLine> lines, TipInput? tip = null, params string[] codes) =>
        new(lines.ToList(), codes, tip ?? TipInput.None);

    public static PriceResult Price(OrderDraft draft, PricingCatalog? catalog = null) =>
        Engine.Price(draft, catalog ?? Spec(), Now);
}
