using System.Text.Json;
using PizzaMagic.Contracts;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.Domain.Tax;
using PizzaMagic.OrderManager.Data.Entities;

namespace PizzaMagic.OrderManager.Services;

/// <summary>
/// Builds the order snapshot rows from a priced breakdown (plan section 4.3): names, prices, rules, allocations, tax
/// lines and the promotion definitions as evaluated, so the order never needs the live catalog again.
/// </summary>
public static class OrderSnapshot
{
    /// <summary>Built scope (A69): every topping is a whole 1x placement.</summary>
    private const string WholeLabel = "whole";
    private const int WholeMask = 15;

    public static CustomerOrderEntity Build(string orderId, PricingOutcome outcome, string? customerName, DateTime nowUtc)
    {
        var breakdown = outcome.Breakdown!;
        var catalog = outcome.Catalog!;
        var menu = catalog.Menu;
        var tierCodes = menu.Tiers.ToDictionary(t => t.Id, t => t.Code);
        var tip = breakdown.NormalizedDraft.Tip;
        var settings = breakdown.Tax.Settings;

        var order = new CustomerOrderEntity
        {
            Id = orderId,
            RestaurantId = menu.RestaurantId,
            ChannelCode = outcome.Channel,
            Status = nameof(OrderStatus.NotStarted),
            Version = 1,
            CustomerName = customerName,
            TaxJurisdictionId = catalog.Chain[^1].Id,
            TimeZoneId = catalog.Zone.Id,
            PricedAtUtc = nowUtc,
            SubtotalCents = breakdown.Subtotal.Cents,
            DiscountCents = breakdown.Discount.Cents,
            TaxableCents = breakdown.Taxable.Cents,
            TaxCents = breakdown.Tax.Tax.Cents,
            TaxRoundingMethod = settings.Method == TaxRoundingMethod.HalfUp ? "HALF_UP" : "TRUNCATE",
            TaxRoundingScope = settings.Scope == TaxRoundingScope.Combined ? "COMBINED" : "PER_LEVEL",
            TipMode = tip.Mode.ToString(),
            TipInput = tip.Mode == TipMode.None ? null : outcome.Tip!.Value,
            TipRatePpm = tip.Mode == TipMode.Percent ? checked((int)tip.Value) : null,
            TipCents = breakdown.Tip.Cents,
            TotalCents = breakdown.Total.Cents,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            StatusChangedAtUtc = nowUtc,
        };

        foreach (var priced in breakdown.Lines)
        {
            var line = priced.Line;
            order.Lines.Add(new OrderLineEntity
            {
                OrderId = orderId,
                LineNumber = priced.LineNumber,
                LineKind = "PIZZA",
                ProductId = line.Size.Id,
                ProductCode = line.Size.Code,
                ProductName = line.Size.DisplayName,
                BasePriceCents = line.Size.Price!.Value.Cents,
                BasePriceSource = line.Size.PriceSource.ToString(),
                TaxClassId = line.Size.TaxClassId,
                CrustProductId = line.Crust.Id,
                CrustCode = line.Crust.Code,
                CrustName = line.Crust.DisplayName,
                CrustIsDefault = line.Crust.Code == menu.DefaultCrust?.Code,
                CrustPriceCents = line.Crust.Price!.Value.Cents,
                CrustTaxClassId = line.Crust.TaxClassId,
                SauceProductId = line.Sauce.Id,
                SauceCode = line.Sauce.Code,
                SauceName = line.Sauce.DisplayName,
                SauceIsDefault = line.Sauce.Code == menu.DefaultSauce?.Code,
                SaucePriceCents = line.Sauce.Price!.Value.Cents,
                SauceTaxClassId = line.Sauce.TaxClassId,
                Quantity = line.Quantity,
                UnitPriceCents = line.UnitPrice.Cents,
                LineTotalCents = line.LineTotal.Cents,
                DiscountCents = breakdown.Allocations.Where(a => a.LineNumber == priced.LineNumber).Sum(a => a.Amount.Cents),
                Toppings = line.Components
                    .Where(c => c.Kind == ComponentKind.Topping)
                    .Select((c, i) => new OrderLineToppingEntity
                    {
                        SortOrder = i + 1,
                        ToppingProductId = c.Product.Id,
                        ToppingCode = c.Product.Code,
                        ToppingName = c.Product.DisplayName,
                        TierCode = tierCodes[c.Product.ToppingTierId!.Value],
                        PortionCode = DraftValidator.WholePortionCode,
                        PortionLabel = WholeLabel,
                        CoverageMask = WholeMask,
                        CoveragePhrase = ToppingPhrase.Of(c.Product.DisplayName, c.Multiplier),
                        PortionTypeCode = DraftValidator.WholePortionCode,
                        Multiplier = c.Multiplier,
                        PriceSource = "GlobalTier",
                        PricingMethod = c.ToppingPrice!.PricingMethod,
                        RatePpm = c.ToppingPrice.Rate?.Ppm,
                        ToppingUnitPriceCents = c.ToppingPrice.UnitPrice.Cents,
                        CoverageFactorPpm = Domain.Monetary.Rate.MaxPpm,
                        CoverageFactorSource = "Global",
                        PlacementUnitChargeCents = c.ToppingPrice.UnitPrice.Cents,
                        PlacementChargeCents = c.UnitCharge.Cents,   // unit x multiplier (UQ29)
                        TaxClassId = c.TaxClassId,
                    })
                    .ToList(),
            });
        }

        var promotionsById = catalog.Promotions.Promotions.ToDictionary(p => p.Id);
        var applied = breakdown.Promotions.Outcomes.Where(o => o.Status == PromoStatus.Applied).ToList();
        foreach (var (outcomeRow, sequence) in applied.Select((o, i) => (o, i + 1)))
        {
            var definition = promotionsById[outcomeRow.PromotionId!.Value];
            var promotion = new OrderPromotionEntity
            {
                OrderId = orderId,
                Sequence = sequence,
                Code = outcomeRow.Code!,
                PromotionId = definition.Id,
                EffectType = definition.EffectType,
                Phase = "Order",
                DiscountCents = outcomeRow.Discount.Cents,
                Message = outcomeRow.Message!.Render(MoneyFormatter.Format),
                DefinitionSnapshot = DefinitionSnapshot(definition, catalog.Promotions),
            };
            foreach (var allocation in breakdown.Allocations.Where(a => a.Code == outcomeRow.Code))
            {
                var line = order.Lines.Single(l => l.LineNumber == allocation.LineNumber);
                promotion.Allocations.Add(new OrderDiscountAllocationEntity
                {
                    OrderLine = line,
                    OrderLineTopping = allocation.Kind == ComponentKind.Topping
                        ? line.Toppings.Single(t => t.ToppingCode == allocation.ProductCode)
                        : null,
                    ComponentKind = allocation.Kind.ToString().ToUpperInvariant(),
                    TaxClassId = allocation.TaxClassId,
                    AmountCents = allocation.Amount.Cents,
                });
            }
            order.Promotions.Add(promotion);
        }

        var jurisdictions = catalog.Chain.Select((node, depth) => (node, depth)).ToDictionary(x => x.node.Id);
        foreach (var taxLine in breakdown.Tax.Lines)
        {
            var (node, depth) = jurisdictions[taxLine.JurisdictionId];
            order.TaxLines.Add(new OrderTaxLineEntity
            {
                OrderId = orderId,
                TaxJurisdictionId = node.Id,
                JurisdictionCode = node.Code,
                JurisdictionName = node.Name,
                JurisdictionDepth = depth,
                TaxClassId = taxLine.TaxClassId,
                TaxClassCode = catalog.TaxClassCodes[taxLine.TaxClassId],
                GrossCents = taxLine.Gross.Cents,
                DiscountCents = taxLine.Discount.Cents,
                TaxableBaseCents = taxLine.Taxable.Cents,
                RatePpm = taxLine.Rate.Ppm,
                RateSource = "Default",
                TaxCents = taxLine.Amount.Cents,
            });
        }

        order.History.Add(new OrderStatusHistoryEntity
        {
            OrderId = orderId,
            FromStatus = null,
            ToStatus = nameof(OrderStatus.NotStarted),
            EventType = "Created",
            Actor = nameof(OrderActor.Customer),
            OrderVersion = 1,
            OccurredAtUtc = nowUtc,
        });
        return order;
    }

    /// <summary>The promotion as evaluated: effect, conditions, priority and conflicts (order_promotion.definition_snapshot).</summary>
    private static string DefinitionSnapshot(PromotionDefinition definition, PromotionCatalog promotions)
    {
        using var effect = JsonDocument.Parse(definition.EffectParams);
        var conditions = definition.Conditions
            .Select(c => new { type = c.Type, @params = JsonDocument.Parse(c.Params).RootElement.Clone() })
            .ToList();
        var incompatibleWith = promotions.Incompatibilities
            .Where(p => p.PromotionIdLow == definition.Id || p.PromotionIdHigh == definition.Id)
            .Select(p => p.PromotionIdLow == definition.Id ? p.PromotionIdHigh : p.PromotionIdLow)
            .Order()
            .ToList();
        return JsonSerializer.Serialize(new
        {
            name = definition.Name,
            effectType = definition.EffectType,
            effectParams = effect.RootElement.Clone(),
            conditions,
            priority = definition.Priority,
            isExclusive = definition.IsExclusive,
            exclusionGroup = definition.ExclusionGroup,
            incompatibleWith,
        });
    }
}
