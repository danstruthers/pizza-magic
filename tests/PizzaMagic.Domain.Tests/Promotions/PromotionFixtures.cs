using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tests.Time;

namespace PizzaMagic.Domain.Tests.Promotions;

/// <summary>
/// The seed promotions (ILOVEPIZZA, SAVE5 and their incompatibility, plan section 4.5) plus test-only promotions.
/// </summary>
public static class PromotionFixtures
{
    /// <summary>Plan default clock: Tuesday 2026-10-27 12:00 PDT.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 27, 19, 0, 0, TimeSpan.Zero);

    public static readonly PromotionDefinition ILovePizza = new(1, "I love pizza", "percent_off_order", """{"ratePpm":100000,"maxDiscountCents":null}""");

    public static readonly PromotionDefinition Save5 = new(2, "Save $5", "fixed_off_order", """{"amountCents":500}""")
    {
        Conditions = [new ConditionDefinition("min_subtotal", """{"amountCents":2000,"inclusive":false}""")],
    };

    public static PromotionCatalog Seed(params PromotionDefinition[] extra) => Catalog(
        [ILovePizza, Save5, .. extra],
        [new PromotionCodeRow("ILOVEPIZZA", 1), new PromotionCodeRow("SAVE5", 2), .. extra.Select(p => new PromotionCodeRow(p.Name, p.Id))],
        [new IncompatiblePair(1, 2)]);

    public static PromotionCatalog Catalog(
        IReadOnlyList<PromotionDefinition> promotions, IReadOnlyList<PromotionCodeRow> codes, IReadOnlyList<IncompatiblePair> pairs) =>
        new(promotions, codes, pairs);

    /// <summary>A test promotion whose code is its name.</summary>
    public static PromotionDefinition Fixed(long id, string code, long cents, int priority = 100) =>
        new(id, code, "fixed_off_order", $$"""{"amountCents":{{cents}}}""", priority);

    public static PromotionContext Context(long grossCents, DateTimeOffset? now = null, long restaurantId = 1, int maxPromoCodes = 3) =>
        new(restaurantId, now ?? DefaultNow, UsTimeZones.LosAngeles(), new Money(grossCents), maxPromoCodes);

    public static PromotionResult Evaluate(PromotionCatalog catalog, PromotionContext context, params string[] codes) =>
        new PromotionEngine(PromotionRegistry.Default, new RejectLaterConflicts()).Evaluate(catalog, codes, context);
}
