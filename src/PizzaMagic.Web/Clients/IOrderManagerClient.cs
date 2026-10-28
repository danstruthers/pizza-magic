using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Clients;

/// <summary>
/// The order builder's only way to the order service (plan section 6.5). The builder never prices anything itself:
/// every total it shows comes from a quote.
/// </summary>
public interface IOrderManagerClient
{
    Task<ApiResult<MenuResponse>> GetMenuAsync(long? restaurantId, CancellationToken cancellationToken);

    Task<ApiResult<QuoteResponse>> QuoteAsync(QuoteRequest draft, CancellationToken cancellationToken);

    /// <summary>Every retry carries the same <paramref name="idempotencyKey"/> and the same body bytes.</summary>
    Task<ApiResult<OrderResponse>> SubmitAsync(SubmitOrderRequest request, string idempotencyKey, CancellationToken cancellationToken);

    Task<ApiResult<OrderResponse>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<ApiResult<OrderStatusResponse>> GetStatusAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Sends If-Match: "version" and the idempotency key, so a retry after a lost response replays.</summary>
    Task<ApiResult<OrderResponse>> UpdateAsync(Guid orderId, int version, UpdateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken);

    Task<ApiResult<OrderResponse>> CancelAsync(Guid orderId, CancellationToken cancellationToken);
}
