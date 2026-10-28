using PizzaMagic.Contracts;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Monetary;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Time;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// Renders a stored order from its snapshot rows only (plan section 4.4): no join to the live catalog, so a placed
/// order looks the same after any price, rate, label or promotion change.
/// </summary>
public static class OrderView
{
    private static readonly string[] CustomerActions = ["update", "cancel"];

    public static OrderResponse ToResponse(CustomerOrderEntity order, TimeZonePeriods zone)
    {
        var status = Enum.Parse<OrderStatus>(order.Status);
        var description = OrderDescriber.Describe(ToDescriptionSource(order));
        var lines = order.Lines.OrderBy(l => l.LineNumber).ToList();

        var pricing = new OrderPricingResponse(
            lines.Select((l, i) => new QuoteLineResponse(
                i,
                l.LineNumber,
                "Pizza",
                l.Quantity,
                new PricedProductResponse(l.ProductCode, l.ProductName, l.BasePriceCents, l.BasePriceSource),
                new PricedChoiceResponse(l.CrustCode!, l.CrustName!, l.CrustPriceCents!.Value, l.CrustIsDefault!.Value),
                new PricedChoiceResponse(l.SauceCode!, l.SauceName!, l.SaucePriceCents!.Value, l.SauceIsDefault!.Value),
                l.Toppings.OrderBy(t => t.SortOrder).Select(t => new PricedToppingResponse(
                    t.ToppingCode, t.ToppingName, t.TierCode, t.PortionCode, t.PortionLabel, t.Multiplier, t.PriceSource,
                    t.ToppingUnitPriceCents, t.CoverageFactorPpm, t.PlacementUnitChargeCents, t.PlacementChargeCents)).ToList(),
                l.UnitPriceCents,
                l.LineTotalCents,
                l.DiscountCents,
                [],
                description.Items[i].Text)).ToList(),
            order.SubtotalCents,
            order.Promotions.OrderBy(p => p.Sequence).Select(p => new PromotionOutcomeResponse(
                p.Code,
                "Applied",
                p.DiscountCents,
                p.Message,
                [],
                // Navigations, not ids: the response is also built before the rows are saved (ids still 0).
                p.Allocations
                    .OrderBy(a => a.OrderLine!.LineNumber)
                    .ThenBy(a => ComponentOrder(a.ComponentKind))
                    .ThenBy(a => a.OrderLineTopping?.SortOrder ?? 0)
                    .Select(a => new AllocationResponse(
                        a.OrderLine!.LineNumber,
                        ComponentName(a.ComponentKind),
                        a.OrderLineTopping?.ToppingCode,
                        a.OrderLineTopping?.PortionCode,
                        a.AmountCents))
                    .ToList())).ToList(),
            order.DiscountCents,
            order.TaxableCents,
            new TaxRoundingResponse(order.TaxRoundingMethod, order.TaxRoundingScope),
            order.TaxLines.OrderBy(t => t.JurisdictionDepth).ThenBy(t => t.TaxClassId).Select(t => new TaxLineResponse(
                t.JurisdictionCode, t.JurisdictionDepth, t.TaxClassCode, t.GrossCents, t.DiscountCents, t.TaxableBaseCents,
                t.RatePpm, t.RateSource, t.TaxCents)).ToList(),
            order.TaxCents,
            Bill(order),
            Tip(order),
            order.TotalCents);

        return new OrderResponse(
            order.Id,
            order.RestaurantId,
            order.ChannelCode,
            order.CustomerName,
            order.Status,
            OrderStateMachine.DisplayName(status),
            order.Version,
            UtcTimestampConverter.Write(order.CreatedAtUtc),
            UtcTimestampConverter.Write(order.StatusChangedAtUtc),
            Local(zone, order.CreatedAtUtc),
            Local(zone, order.StatusChangedAtUtc),
            AllowedActions(status),
            pricing,
            DescriptionTextRenderer.Render(description));
    }

    public static OrderStatusResponse ToStatus(CustomerOrderEntity order, TimeZonePeriods zone)
    {
        var status = Enum.Parse<OrderStatus>(order.Status);
        return new OrderStatusResponse(
            order.Id,
            order.Status,
            OrderStateMachine.DisplayName(status),
            order.Version,
            UtcTimestampConverter.Write(order.StatusChangedAtUtc),
            Local(zone, order.StatusChangedAtUtc),
            AllowedActions(status));
    }

    public static string DescriptionText(CustomerOrderEntity order) =>
        DescriptionTextRenderer.Render(OrderDescriber.Describe(ToDescriptionSource(order)));

    public static IReadOnlyList<string> AllowedActions(OrderStatus status) =>
        OrderStateMachine.IsCustomerModifiable(status) ? CustomerActions : [];

    /// <summary>The never-stored display companion of a UTC instant in the order's zone; null outside coverage.</summary>
    public static LocalTimeResponse? Local(TimeZonePeriods zone, DateTime utc) =>
        LocalTimeResolver.ToLocal(zone, new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))) is { } local
            ? new LocalTimeResponse(LocalWallClockConverter.Write(local.LocalTime), local.UtcOffsetMinutes, local.Abbreviation)
            : null;

    private static DescriptionSource ToDescriptionSource(CustomerOrderEntity order) => new(
        order.Lines.OrderBy(l => l.LineNumber).Select(l => new DescribedPizzaLine(
            l.Quantity, l.ProductName, l.CrustName!, l.CrustIsDefault!.Value, l.SauceName!, l.SauceIsDefault!.Value,
            l.Toppings.OrderBy(t => t.SortOrder).Select(t => t.CoveragePhrase).ToList(),
            l.LineTotalCents)).ToList(),
        order.Promotions.OrderBy(p => p.Sequence).Select(p => new DescribedPromotion(p.Code, p.DiscountCents)).ToList(),
        order.TaxCents,
        Enum.Parse<TipMode>(order.TipMode),
        TipRateBasisPoints(order),
        order.TipCents,
        order.TotalCents);

    private static long Bill(CustomerOrderEntity order) => order.TaxableCents + order.TaxCents;

    private static TipResponse Tip(CustomerOrderEntity order) => new(
        order.TipMode,
        order.TipInput,
        order.TipRatePpm,
        order.TipCents,
        order.TipMode == nameof(TipMode.Amount) ? TipRateBasisPoints(order) : null);

    private static long? TipRateBasisPoints(CustomerOrderEntity order) => Enum.Parse<TipMode>(order.TipMode) switch
    {
        TipMode.Percent => Rounding.RoundHalfAwayFromZero(order.TipRatePpm!.Value, Rate.MaxPpm / TipCalculator.BasisPointScale),
        TipMode.Amount => TipCalculator.EffectiveRateBasisPoints(new TipInput(TipMode.Amount, order.TipCents), new Money(order.TipCents), new Money(Bill(order))),
        _ => null,
    };

    private static int ComponentOrder(string kind) => kind switch { "BASE" => 0, "CRUST" => 1, "SAUCE" => 2, _ => 3 };

    private static string ComponentName(string kind) => kind switch { "BASE" => "Base", "CRUST" => "Crust", "SAUCE" => "Sauce", _ => "Topping" };
}
