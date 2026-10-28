using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.Web.Clients;

/// <summary>
/// Typed access to the order service over HTTP. Business refusals come back as problems and transport failures as
/// Unavailable; neither throws to a controller. Only the caller's own cancellation propagates.
/// </summary>
public sealed class OrderManagerClient(IHttpClientFactory factory) : IOrderManagerClient
{
    public const string ClientName = "OrderManager";
    public const string QuotesClientName = "OrderManagerQuotes";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<MenuResponse>> GetMenuAsync(long? restaurantId, CancellationToken cancellationToken) =>
        SendAsync<MenuResponse>(ClientName, HttpMethod.Get, restaurantId is { } id ? $"/api/v1/menu?restaurantId={id}" : "/api/v1/menu", null, null, cancellationToken);

    public Task<ApiResult<QuoteResponse>> QuoteAsync(QuoteRequest draft, CancellationToken cancellationToken) =>
        SendAsync<QuoteResponse>(QuotesClientName, HttpMethod.Post, "/api/v1/quotes", draft, null, cancellationToken);

    public Task<ApiResult<OrderResponse>> SubmitAsync(SubmitOrderRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        SendAsync<OrderResponse>(ClientName, HttpMethod.Post, "/api/v1/orders", request,
            message => message.Headers.Add("Idempotency-Key", idempotencyKey), cancellationToken);

    public Task<ApiResult<OrderResponse>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        SendAsync<OrderResponse>(ClientName, HttpMethod.Get, $"/api/v1/orders/{orderId}", null, null, cancellationToken);

    public Task<ApiResult<OrderStatusResponse>> GetStatusAsync(Guid orderId, CancellationToken cancellationToken) =>
        SendAsync<OrderStatusResponse>(ClientName, HttpMethod.Get, $"/api/v1/orders/{orderId}/status", null, null, cancellationToken);

    public Task<ApiResult<OrderResponse>> UpdateAsync(
        Guid orderId, int version, UpdateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        SendAsync<OrderResponse>(ClientName, HttpMethod.Put, $"/api/v1/orders/{orderId}", request,
            message =>
            {
                message.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
                message.Headers.Add("Idempotency-Key", idempotencyKey);
            },
            cancellationToken);

    public Task<ApiResult<OrderResponse>> CancelAsync(Guid orderId, CancellationToken cancellationToken) =>
        SendAsync<OrderResponse>(ClientName, HttpMethod.Post, $"/api/v1/orders/{orderId}/cancel", null, null, cancellationToken);

    public Task<ApiResult<KitchenQueueResponse>> GetKitchenQueueAsync(long? restaurantId, CancellationToken cancellationToken) =>
        SendAsync<KitchenQueueResponse>(ClientName, HttpMethod.Get,
            restaurantId is { } id ? $"/api/v1/kitchen/orders?restaurantId={id}" : "/api/v1/kitchen/orders", null, null, cancellationToken);

    // Safe to retry: a repeat with the same expected status is answered with alreadyApplied, never a second step.
    public Task<ApiResult<AdvanceOrderResponse>> AdvanceAsync(Guid orderId, string expectedStatus, CancellationToken cancellationToken) =>
        SendAsync<AdvanceOrderResponse>(ClientName, HttpMethod.Post, $"/api/v1/kitchen/orders/{orderId}/advance",
            new AdvanceOrderRequest(expectedStatus), null, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(
        string clientName, HttpMethod method, string path, object? body, Action<HttpRequestMessage>? headers, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            // Serialized once: every retry sends the same bytes, which the server's request hash relies on.
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, Json));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        headers?.Invoke(request);

        try
        {
            using var response = await factory.CreateClient(clientName).SendAsync(request, cancellationToken);
            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // the caller gave up (e.g. the browser closed the request): not an outage
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                     or OperationCanceledException or JsonException)
        {
            return ApiResult<T>.Unavailable(e.Message);
        }
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw new JsonException("The order service returned an empty body.");
            var replayed = response.Headers.TryGetValues("Idempotent-Replayed", out var flags) && flags.Contains("true");
            return ApiResult<T>.Success(value, response.Headers.ETag?.Tag, replayed);
        }
        if (status >= 500)
        {
            return ApiResult<T>.Unavailable($"The order service answered {status}.");
        }
        return ApiResult<T>.Failed(await ReadProblemAsync(response, status, cancellationToken));
    }

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response, int status, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement body;
        try
        {
            body = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone();
        }
        catch (JsonException)
        {
            body = JsonDocument.Parse("{}").RootElement.Clone();
        }

        var errors = new Dictionary<string, IReadOnlyList<FieldErrorResponse>>();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("errors", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject())
            {
                errors[entry.Name] = entry.Value.Deserialize<List<FieldErrorResponse>>(Json) ?? [];
            }
        }
        return new ApiProblem(
            String(body, "type") ?? "about:blank",
            status,
            String(body, "title"),
            String(body, "detail"),
            errors,
            body);
    }

    private static string? String(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// Registration (plan section 6.5): the standard resilience handler on both clients. POST and PUT retries are
/// deliberately allowed (DisableForUnsafeHttpMethods is not called): they are safe only because every mutation
/// carries an Idempotency-Key, so a retried submit or update replays instead of acting twice. 4xx is never retried.
/// </summary>
public static class OrderManagerClientRegistration
{
    public static IServiceCollection AddOrderManagerClient(
        this IServiceCollection services, IConfiguration configuration, Func<HttpMessageHandler>? primaryHandler = null)
    {
        var baseUrl = new Uri(configuration["OrderManager:BaseUrl"] ?? "http://localhost:5100");
        var retryDelay = TimeSpan.FromMilliseconds(configuration.GetValue("OrderManager:RetryBaseDelayMilliseconds", 250));

        Register(OrderManagerClient.ClientName, attempt: TimeSpan.FromSeconds(3), total: TimeSpan.FromSeconds(10), retries: 3);
        // Quotes: a tighter budget. A stale total is better than a hung page.
        Register(OrderManagerClient.QuotesClientName, attempt: TimeSpan.FromSeconds(2), total: TimeSpan.FromSeconds(4), retries: 1);

        services.AddSingleton<IOrderManagerClient, OrderManagerClient>();
        return services;

        void Register(string name, TimeSpan attempt, TimeSpan total, int retries)
        {
            var client = services.AddHttpClient(name, c => c.BaseAddress = baseUrl);
            if (primaryHandler is not null)
            {
                client.ConfigurePrimaryHttpMessageHandler(primaryHandler);
            }
            client.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = attempt;
                options.TotalRequestTimeout.Timeout = total;
                options.Retry.MaxRetryAttempts = retries;
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.Delay = retryDelay;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(30).Ticks, attempt.Ticks * 2));
            });
        }
    }
}
