using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;

namespace PizzaMagic.Web.Tests.Builder;

/// <summary>A scripted order service: tests set the next results and read back what the order builder sent.</summary>
public sealed class FakeOrderManagerClient : IOrderManagerClient
{
    public ApiResult<MenuResponse> Menu { get; set; } = ApiResult<MenuResponse>.Success(Menus.Spec(), null, false);

    public Func<QuoteRequest, ApiResult<QuoteResponse>> Quote { get; set; } = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(533), null, false);

    public List<QuoteRequest> QuoteRequests { get; } = [];

    public Task<ApiResult<MenuResponse>> GetMenuAsync(long? restaurantId, CancellationToken cancellationToken) => Task.FromResult(Menu);

    public Task<ApiResult<QuoteResponse>> QuoteAsync(QuoteRequest draft, CancellationToken cancellationToken)
    {
        QuoteRequests.Add(draft);
        return Task.FromResult(Quote(draft));
    }

    public Task<ApiResult<OrderResponse>> SubmitAsync(SubmitOrderRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by the builder (M14).");

    public Task<ApiResult<OrderResponse>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ApiResult<OrderStatusResponse>> GetStatusAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ApiResult<OrderResponse>> UpdateAsync(Guid orderId, int version, UpdateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ApiResult<OrderResponse>> CancelAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
            new("BASIC", "Basic", [new("PEPPERONI", "Pepperoni", Grid(("SMALL", 40), ("MEDIUM", 56), ("LARGE", 96)))]),
            new("PREMIUM", "Premium", [new("BACON", "Bacon", Grid(("SMALL", 55), ("MEDIUM", 77), ("LARGE", 132)))]),
        ],
        new MenuPolicyResponse(20, 20, 12, 3, 50_000, 1_000_000, [150_000, 180_000, 200_000]));
}

public static class Quotes
{
    public static QuoteResponse Simple(long total, bool canSubmit = true, IReadOnlyList<string>? normalizedCodes = null) => new(
        1, "ONLINE", [], total, [], normalizedCodes ?? [], 0, total, new TaxRoundingResponse("HALF_UP", "COMBINED"), [], 0, total,
        new TipResponse("None", null, null, 0, null), total, [],
        new NormalizedDraftResponse("ONLINE", [], normalizedCodes ?? [], null), canSubmit, $"Total: {total}");
}
