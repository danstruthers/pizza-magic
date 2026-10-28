using PizzaMagic.Contracts;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Tax;

namespace PizzaMagic.OrderManager.Services;

/// <summary>Maps Domain results onto the wire contract. Every number is the Domain's; nothing is recomputed here.</summary>
public static class ResponseMapper
{
    /// <summary>Built scope (A69): every topping is a whole 1x placement at the WHOLE factor.</summary>
    private const string WholeLabel = "whole";
    private const string GlobalTierSource = "GlobalTier";

    public static QuoteResponse ToQuote(PricingOutcome outcome)
    {
        var breakdown = outcome.Breakdown!;
        var catalog = outcome.Catalog!;
        var menu = catalog.Menu;
        var description = OrderDescriber.Describe(DescriptionSource.FromBreakdown(breakdown, menu));
        var tierCodes = menu.Tiers.ToDictionary(t => t.Id, t => t.Code);

        var lines = breakdown.Lines.Select((l, i) => new QuoteLineResponse(
            i,
            l.LineNumber,
            "Pizza",
            l.Line.Quantity,
            new PricedProductResponse(l.Line.Size.Code, l.Line.Size.DisplayName, l.Line.Size.Price!.Value.Cents, l.Line.Size.PriceSource.ToString()),
            Choice(l.Line.Crust, menu.DefaultCrust),
            Choice(l.Line.Sauce, menu.DefaultSauce),
            l.Line.Components.Where(c => c.Kind == ComponentKind.Topping).Select(c => new PricedToppingResponse(
                c.Product.Code,
                c.Product.DisplayName,
                tierCodes[c.Product.ToppingTierId!.Value],
                DraftValidator.WholePortionCode,
                WholeLabel,
                c.Multiplier,
                GlobalTierSource,
                c.ToppingPrice!.UnitPrice.Cents,
                Domain.Monetary.Rate.MaxPpm,
                c.ToppingPrice.UnitPrice.Cents,   // whole coverage: the placement unit charge is the topping unit price
                c.UnitCharge.Cents)).ToList(),    // x multiplier (UQ29)
            l.Line.UnitPrice.Cents,
            l.Line.LineTotal.Cents,
            breakdown.Allocations.Where(a => a.LineNumber == l.LineNumber).Sum(a => a.Amount.Cents),
            [],
            description.Items[i].Text)).ToList();

        var promotions = breakdown.Promotions.Outcomes.Select(o => new PromotionOutcomeResponse(
            o.Code ?? o.EnteredCode,
            o.Status.ToString(),
            o.Discount.Cents,
            o.Message?.Render(MoneyFormatter.Format),
            o.ConflictsWith,
            breakdown.Allocations
                .Where(a => o.Status == Domain.Promotions.PromoStatus.Applied && a.Code == o.Code)
                .Select(a => new AllocationResponse(
                    a.LineNumber,
                    a.Kind.ToString(),
                    a.Kind == ComponentKind.Topping ? a.ProductCode : null,
                    a.Kind == ComponentKind.Topping ? DraftValidator.WholePortionCode : null,
                    a.Amount.Cents))
                .ToList())).ToList();

        var tax = breakdown.Tax;
        var taxLines = tax.Lines.Select(t => new TaxLineResponse(
            t.JurisdictionCode, t.Depth, catalog.TaxClassCodes[t.TaxClassId], t.Gross.Cents, t.Discount.Cents,
            t.Taxable.Cents, t.Rate.Ppm, "Default", t.Amount.Cents)).ToList();

        var tipInput = breakdown.NormalizedDraft.Tip;
        var tip = new TipResponse(
            tipInput.Mode.ToString(),
            outcome.Tip?.Value,
            tipInput.Mode == TipMode.Percent ? checked((int)tipInput.Value) : null,
            breakdown.Tip.Cents,
            breakdown.TipEffectiveRateBasisPoints);

        var normalizedDraft = new NormalizedDraftResponse(
            outcome.Channel,
            breakdown.NormalizedDraft.LineItems.Select(l => new LineItemRequest(
                "Pizza", l.SizeCode, l.Quantity,
                l.Toppings.Select(t => new ToppingRequest(t.Code, t.Portion, t.Multiplier)).ToList(),
                l.CrustCode, l.SauceCode)).ToList(),
            breakdown.NormalizedDraft.PromoCodes,
            outcome.Tip);

        return new QuoteResponse(
            menu.RestaurantId,
            outcome.Channel,
            lines,
            breakdown.Subtotal.Cents,
            promotions,
            breakdown.Promotions.NormalizedPromoCodes,
            breakdown.Discount.Cents,
            breakdown.Taxable.Cents,
            new TaxRoundingResponse(Method(tax.Settings.Method), Scope(tax.Settings.Scope)),
            taxLines,
            tax.Tax.Cents,
            breakdown.Bill.Cents,
            tip,
            breakdown.Total.Cents,
            breakdown.Issues.Select(i => new IssueResponse(i.Code, i.Message)).ToList(),
            normalizedDraft,
            breakdown.CanSubmit,
            DescriptionTextRenderer.Render(description));
    }

    /// <summary>The menu with prices computed by the same EffectiveMenu functions the engine charges with.</summary>
    public static MenuResponse ToMenu(PricingCatalog catalog)
    {
        var menu = catalog.Menu;
        var sizes = menu.Available(ProductType.PizzaSize);
        var toppings = menu.Available(ProductType.Topping);

        var tiers = menu.Tiers.Select(tier => new MenuTierResponse(
            tier.Code,
            tier.DisplayName,
            toppings.Where(t => t.ToppingTierId == tier.Id).Select(topping => new MenuToppingResponse(
                topping.Code,
                topping.DisplayName,
                sizes.ToDictionary(
                    size => size.Code,
                    size => (IReadOnlyDictionary<string, long>)new Dictionary<string, long>
                    {
                        [DraftValidator.WholePortionCode] = menu.ToppingUnitPrice(topping, size.Price!.Value).UnitPrice.Cents,
                    }),
                topping.MaxMultiplier ?? 1)).ToList())).ToList();

        var policy = catalog.Policy;
        return new MenuResponse(
            menu.RestaurantId,
            catalog.Restaurant?.Name ?? "",
            sizes.Select(s => new MenuSizeResponse(s.Code, s.DisplayName, s.Price!.Value.Cents)).ToList(),
            menu.Available(ProductType.Crust).Select(c => new MenuChoiceResponse(c.Code, c.DisplayName, c.Price!.Value.Cents, c.Code == menu.DefaultCrust?.Code)).ToList(),
            menu.Available(ProductType.Sauce).Select(s => new MenuChoiceResponse(s.Code, s.DisplayName, s.Price!.Value.Cents, s.Code == menu.DefaultSauce?.Code)).ToList(),
            [],
            tiers,
            new MenuPolicyResponse(policy.MaxLines, policy.MaxLineQuantity, policy.MaxToppingsPerPizza, policy.MaxPromoCodes,
                policy.MaxTipAmountCents, policy.MaxTipPercentPpm, policy.TipPresetsPpm));
    }

    private static PricedChoiceResponse Choice(EffectiveProduct product, EffectiveProduct? fallback) =>
        new(product.Code, product.DisplayName, product.Price!.Value.Cents, product.Code == fallback?.Code);

    private static string Method(TaxRoundingMethod method) => method == TaxRoundingMethod.HalfUp ? "HALF_UP" : "TRUNCATE";

    private static string Scope(TaxRoundingScope scope) => scope == TaxRoundingScope.Combined ? "COMBINED" : "PER_LEVEL";
}
