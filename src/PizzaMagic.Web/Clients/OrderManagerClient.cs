using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Clients;

/// <summary>M12 red-phase stub.</summary>
public sealed class OrderManagerClient : IOrderManagerClient
{
    public Task<ApiResult<MenuResponse>> GetMenuAsync(long? restaurantId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ApiResult<QuoteResponse>> QuoteAsync(QuoteRequest draft, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ApiResult<OrderResponse>> SubmitAsync(SubmitOrderRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ApiResult<OrderResponse>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ApiResult<OrderStatusResponse>> GetStatusAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ApiResult<OrderResponse>> UpdateAsync(Guid orderId, int version, UpdateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ApiResult<OrderResponse>> CancelAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotImplementedException();
}

/// <summary>M12 red-phase stub.</summary>
public static class OrderManagerClientRegistration
{
    public static IServiceCollection AddOrderManagerClient(this IServiceCollection services, IConfiguration configuration, Func<HttpMessageHandler>? primaryHandler = null)
    {
        services.AddSingleton<IOrderManagerClient, OrderManagerClient>();
        return services;
    }
}
