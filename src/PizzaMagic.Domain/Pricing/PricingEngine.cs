using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;

namespace PizzaMagic.Domain.Pricing;

/// <summary>
/// The single source of truth for every total (plan section 2.3): the quote the customer watches and the
/// charge the restaurant stores both come from this method on the same catalog rows. Pure and deterministic;
/// every number comes from the catalog.
/// </summary>
public sealed class PricingEngine(PromotionEngine promotions)
{
    private sealed record Component(int LineNumber, PricedComponent Priced, long Gross)
    {
        public long Net { get; set; } = Gross;
    }

    public PriceResult Price(OrderDraft draft, PricingCatalog catalog, PricingContext context)
    {
        // 0. Validate: all field errors, with paths.
        var errors = DraftValidator.Validate(draft, catalog);
        if (errors.Count > 0)
        {
            return new PriceResult(errors, null);
        }

        // 1-2. Price lines; S = sum of line totals.
        var lines = draft.LineItems
            .Select((line, i) => new PricedLine(i + 1, PizzaLinePricer.Price(catalog.Menu, ToRequest(line))))
            .ToList();
        var subtotal = lines.Aggregate(Money.Zero, (sum, l) => sum + l.Line.LineTotal);

        // 3-4. Promotions on the gross subtotal, then allocate each applied discount over component nets.
        var promotionContext = new PromotionContext(catalog.Menu.RestaurantId, context.NowUtc, catalog.Zone, subtotal, catalog.Policy.MaxPromoCodes);
        var promotionResult = promotions.Evaluate(catalog.Promotions, draft.PromoCodes, promotionContext);
        var components = lines
            .SelectMany(l => l.Line.Components.Select(c => new Component(l.LineNumber, c, checked(c.UnitCharge.Cents * l.Line.Quantity))))
            .ToList();
        var allocations = Allocate(promotionResult.AppliedInOrder, components);
        var discount = promotionResult.TotalDiscount;

        // 5-6. Class bases and tax.
        var bases = components
            .GroupBy(c => c.Priced.TaxClassId)
            .Select(g => new ClassBase(g.Key, SortOrder: 0, new Money(g.Sum(c => c.Gross)), new Money(g.Sum(c => c.Gross - c.Net))))
            .ToList();
        var tax = TaxCalculator.Calculate(bases, catalog.Chain, TaxRoundingSettings.Resolve(catalog.Chain));

        // 7-9. Bill, tip, total. An empty order has nothing to tip on.
        var taxable = subtotal - discount;
        var bill = taxable + tax.Tax;
        var tipInput = lines.Count == 0 ? TipInput.None : draft.Tip;
        var tip = TipCalculator.Calculate(tipInput, bill);
        var total = bill + tip;

        // 11. Normalized draft and canSubmit.
        var issues = lines.Count == 0 ? new List<PricingIssue> { new("ORDER_EMPTY", "Add at least one pizza to your order.") } : [];
        var normalized = new OrderDraft(lines.Select(l => Normalize(l.Line)).ToList(), promotionResult.NormalizedPromoCodes, draft.Tip);
        var canSubmit = lines.Count > 0
            && promotionResult.Outcomes
                .Where(o => o.Status != PromoStatus.Duplicate && o.Code is not null && promotionResult.NormalizedPromoCodes.Contains(o.Code))
                .All(o => o.Status == PromoStatus.Applied);   // a repeat collapses into the held code and blocks nothing

        var breakdown = new PriceBreakdown(
            lines, subtotal, promotionResult, discount, allocations, taxable, tax, bill, tip,
            TipCalculator.EffectiveRateBasisPoints(tipInput, tip, bill), total, issues, normalized, canSubmit);
        return new PriceResult([], breakdown);
    }

    /// <summary>
    /// Splits each order-scoped discount over every (line, component) net with LargestRemainder, in effect order,
    /// so the allocations sum exactly to each discount and no net goes negative (plan section 2.8).
    /// </summary>
    private static List<DiscountAllocation> Allocate(IReadOnlyList<CodeOutcome> appliedInOrder, List<Component> components)
    {
        var allocations = new List<DiscountAllocation>();
        foreach (var applied in appliedInOrder)
        {
            var shares = LargestRemainder.Allocate(applied.Discount.Cents, components.Select(c => c.Net).ToList());
            for (var i = 0; i < components.Count; i++)
            {
                if (shares[i] == 0)
                {
                    continue;
                }
                var component = components[i];
                component.Net -= shares[i];
                allocations.Add(new DiscountAllocation(
                    applied.Code!, applied.PromotionId!.Value, component.LineNumber, component.Priced.Kind,
                    component.Priced.Product.Code, component.Priced.TaxClassId, new Money(shares[i])));
            }
        }
        return allocations;
    }

    private static PizzaLineRequest ToRequest(PizzaDraftLine line) => new(
        DraftValidator.Canonical(line.SizeCode),
        line.Quantity,
        line.Toppings.Select(t => DraftValidator.Canonical(t.Code)).ToList(),
        line.CrustCode is null ? null : DraftValidator.Canonical(line.CrustCode),
        line.SauceCode is null ? null : DraftValidator.Canonical(line.SauceCode),
        line.Toppings.ToDictionary(t => DraftValidator.Canonical(t.Code), t => t.Multiplier ?? 1));

    /// <summary>The line as the server accepts it: canonical codes, explicit crust and sauce, toppings in display order.</summary>
    private static PizzaDraftLine Normalize(PricedPizzaLine line) => new(
        line.Size.Code,
        line.Quantity,
        line.Components
            .Where(c => c.Kind == ComponentKind.Topping)
            .Select(c => new DraftTopping(c.Product.Code, DraftValidator.WholePortionCode, c.Multiplier))
            .ToList(),
        line.Crust.Code,
        line.Sauce.Code);
}
