using System.Text.Json;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;

namespace PizzaMagic.Web.Tests.Builder;

/// <summary>A scripted order service: tests set the next results and read back what the order builder sent.</summary>
public sealed class FakeOrderManagerClient : IOrderManagerClient
{
    public ApiResult<MenuResponse> Menu { get; set; } = ApiResult<MenuResponse>.Success(Menus.Spec(), null, false);

    public Func<QuoteRequest, ApiResult<QuoteResponse>> Quote { get; set; } = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(533), null, false);

    public List<QuoteRequest> QuoteRequests { get; } = [];

    public List<long?> MenuRequests { get; } = [];

    public Task<ApiResult<MenuResponse>> GetMenuAsync(long? restaurantId, CancellationToken cancellationToken)
    {
        MenuRequests.Add(restaurantId);
        return Task.FromResult(Menu);
    }

    public Task<ApiResult<QuoteResponse>> QuoteAsync(QuoteRequest draft, CancellationToken cancellationToken)
    {
        QuoteRequests.Add(draft);
        return Task.FromResult(Quote(draft));
    }

    public Func<SubmitOrderRequest, string, ApiResult<OrderResponse>> Submit { get; set; } =
        (_, _) => ApiResult<OrderResponse>.Success(SampleOrders.Order(), "\"1\"", false);

    public List<(SubmitOrderRequest Request, string Key)> SubmitRequests { get; } = [];

    public Func<Guid, ApiResult<OrderResponse>> Order { get; set; } = id => ApiResult<OrderResponse>.Success(SampleOrders.Order(id: id), "\"1\"", false);

    public Func<Guid, ApiResult<OrderStatusResponse>> Status { get; set; } = id => ApiResult<OrderStatusResponse>.Success(SampleOrders.Status(id: id), "\"1\"", false);

    public Func<Guid, int, UpdateOrderRequest, string, ApiResult<OrderResponse>> Update { get; set; } =
        (id, version, _, _) => ApiResult<OrderResponse>.Success(SampleOrders.Order(version: version + 1, id: id), null, false);

    public List<(Guid Id, int Version, UpdateOrderRequest Request, string Key)> UpdateRequests { get; } = [];

    public Func<Guid, ApiResult<OrderResponse>> Cancel { get; set; } =
        id => ApiResult<OrderResponse>.Success(SampleOrders.Order("Cancelled", 2, id: id), null, false);

    public List<(Guid Id, int? Version)> CancelRequests { get; } = [];

    /// <summary>The cancellation token each submit or update was given (a placed order must not depend on the browser staying connected).</summary>
    public List<CancellationToken> WriteTokens { get; } = [];

    public Task<ApiResult<OrderResponse>> SubmitAsync(SubmitOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        WriteTokens.Add(cancellationToken);
        SubmitRequests.Add((request, idempotencyKey));
        return Task.FromResult(Submit(request, idempotencyKey));
    }

    public Task<ApiResult<OrderResponse>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult(Order(orderId));

    public Task<ApiResult<OrderStatusResponse>> GetStatusAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult(Status(orderId));

    public Task<ApiResult<OrderResponse>> UpdateAsync(Guid orderId, int version, UpdateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        WriteTokens.Add(cancellationToken);
        UpdateRequests.Add((orderId, version, request, idempotencyKey));
        return Task.FromResult(Update(orderId, version, request, idempotencyKey));
    }

    public Task<ApiResult<OrderResponse>> CancelAsync(Guid orderId, int? version, CancellationToken cancellationToken)
    {
        CancelRequests.Add((orderId, version));
        return Task.FromResult(Cancel(orderId));
    }

    public Func<long?, ApiResult<KitchenQueueResponse>> KitchenQueue { get; set; } =
        _ => ApiResult<KitchenQueueResponse>.Success(new KitchenQueueResponse(1, [SampleOrders.Queued()]), null, false);

    public Func<Guid, string, ApiResult<AdvanceOrderResponse>> Advance { get; set; } =
        (id, expected) => ApiResult<AdvanceOrderResponse>.Success(
            new AdvanceOrderResponse(id.ToString(), expected, "Started", 2, false, [], ""), null, false);

    public List<(Guid Id, string ExpectedStatus)> AdvanceRequests { get; } = [];

    public Task<ApiResult<KitchenQueueResponse>> GetKitchenQueueAsync(long? restaurantId, CancellationToken cancellationToken) =>
        Task.FromResult(KitchenQueue(restaurantId));

    public Task<ApiResult<AdvanceOrderResponse>> AdvanceAsync(Guid orderId, string expectedStatus, CancellationToken cancellationToken)
    {
        AdvanceRequests.Add((orderId, expectedStatus));
        return Task.FromResult(Advance(orderId, expectedStatus));
    }
}

public static class Menus
{
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Grid(params (string Size, long Price)[] prices) =>
        prices.ToDictionary(p => p.Size, p => (IReadOnlyDictionary<string, long>)new Dictionary<string, long> { ["WHOLE"] = p.Price });

    public static MenuResponse Spec(params MenuSizeResponse[] extraSizes) => new(
        1,
        "Pizza Magic",
        [new("SMALL", "Small", 500), new("MEDIUM", "Medium", 700), new("LARGE", "Large", 1200), .. extraSizes],
        [new("HAND_TOSSED", "Hand tossed", 0, true)],
        [new("CLASSIC_TOMATO", "Classic tomato", 0, true)],
        [],
        [
            new("BASIC", "Basic", [new("PEPPERONI", "Pepperoni", Grid(("SMALL", 40), ("MEDIUM", 56), ("LARGE", 96)), MaxMultiplier: 3)]),
            new("PREMIUM", "Premium", [new("BACON", "Bacon", Grid(("SMALL", 55), ("MEDIUM", 77), ("LARGE", 132)))]),
        ],
        new MenuPolicyResponse(20, 20, 12, 3, 50_000, 1_000_000, [150_000, 180_000, 200_000]));
}

public static class Quotes
{
    /// <summary>A Large pizza with Pepperoni as the server normalizes it: explicit crust, sauce, portion and multiplier.</summary>
    public static readonly LineItemRequest NormalizedLarge =
        new("Pizza", "LARGE", 1, [new ToppingRequest("PEPPERONI", "WHOLE", 1)], "HAND_TOSSED", "CLASSIC_TOMATO");

    public static QuoteResponse Simple(
        long total,
        bool canSubmit = true,
        IReadOnlyList<string>? normalizedCodes = null,
        IReadOnlyList<LineItemRequest>? normalizedLines = null,
        IReadOnlyList<PromotionOutcomeResponse>? promotions = null,
        IReadOnlyList<IssueResponse>? issues = null) => new(
        1, "ONLINE", [], total, promotions ?? [], normalizedCodes ?? [], 0, total, new TaxRoundingResponse("HALF_UP", "COMBINED"), [], 0, total,
        new TipResponse("None", null, null, 0, null), total, issues ?? [],
        new NormalizedDraftResponse("ONLINE", normalizedLines ?? [], normalizedCodes ?? [], null), canSubmit, $"Total: {total}");
}

public static class SampleOrders
{
    public static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static readonly QuoteLineResponse LargePepperoni = new(
        0, 1, "Pizza", 1,
        new PricedProductResponse("LARGE", "Large", 1200, "Global"),
        new PricedChoiceResponse("HAND_TOSSED", "Hand tossed", 0, true),
        new PricedChoiceResponse("CLASSIC_TOMATO", "Classic tomato", 0, true),
        [new PricedToppingResponse("PEPPERONI", "Pepperoni", "BASIC", "WHOLE", "whole", 1, "GlobalTier", 96, 1_000_000, 96, 96)],
        1296, 1296, 0, [], "1 x Large pizza with Pepperoni");

    private static string Display(string status) => status switch
    {
        "NotStarted" => "Not Started",
        "ReadyForPickup" => "Ready for Pickup",
        _ => status,
    };

    private static IReadOnlyList<string> Actions(string status) => status == "NotStarted" ? ["update", "cancel"] : [];

    /// <summary>1 Large Pepperoni: subtotal $12.96, tax $0.84 (6.5%), total $13.80.</summary>
    public static OrderResponse Order(string status = "NotStarted", int version = 1, Guid? id = null, long restaurantId = 1) => new(
        (id ?? Id).ToString(), restaurantId, "ONLINE", null, status, Display(status), version,
        "2026-10-28T19:00:00.000Z", "2026-10-28T19:05:00.000Z",
        new LocalTimeResponse("2026-10-28T12:00:00", -420, "PDT"), new LocalTimeResponse("2026-10-28T12:05:00", -420, "PDT"),
        Actions(status),
        new OrderPricingResponse([LargePepperoni], 1296, [], 0, 1296, new TaxRoundingResponse("HALF_UP", "COMBINED"), [], 84, 1380,
            new TipResponse("None", null, null, 0, null), 1380),
        "1 x Large pizza with Pepperoni\nTax: $0.84\nTotal: $13.80");

    /// <summary>The sample order as the kitchen queue lists it.</summary>
    public static KitchenOrderResponse Queued(string status = "NotStarted", string next = "Started", string nextDisplay = "Started", Guid? id = null) => new(
        (id ?? Id).ToString(), status, Display(status), 1, "2026-10-28T19:00:00.000Z", new LocalTimeResponse("2026-10-28T12:00:00", -420, "PDT"),
        "Dana", ["1 x Large pizza with Pepperoni 2x", "2 x Small pizza (no toppings)"], 1380, next, nextDisplay);

    public static OrderStatusResponse Status(string status = "Started", int version = 2, Guid? id = null) => new(
        (id ?? Id).ToString(), status, Display(status), version, "2026-10-28T19:05:00.000Z",
        new LocalTimeResponse("2026-10-28T12:05:00", -420, "PDT"), Actions(status));
}

public static class Problems
{
    public static ApiProblem Of(
        string type,
        int status,
        string? detail = null,
        object? extensions = null,
        IReadOnlyDictionary<string, IReadOnlyList<FieldErrorResponse>>? errors = null) =>
        new(type, status, null, detail, errors ?? new Dictionary<string, IReadOnlyList<FieldErrorResponse>>(),
            JsonSerializer.SerializeToElement(extensions ?? new { }, WebFactory.Json));
}
