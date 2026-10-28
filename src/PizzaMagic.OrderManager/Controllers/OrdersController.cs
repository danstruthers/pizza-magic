using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Controllers;

/// <summary>Submit an order (idempotent) and read it back from its snapshot.</summary>
[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    private const string ReplayedHeader = "Idempotent-Replayed";

    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await orders.SubmitAsync(request, idempotencyKey, cancellationToken);
        switch (result.Outcome)
        {
            case SubmitOutcome.Created:
            case SubmitOutcome.Replayed:
                Response.Headers.Location = $"/api/v1/orders/{result.OrderId}";
                Response.Headers.ETag = result.ETag;
                if (result.Outcome == SubmitOutcome.Replayed)
                {
                    Response.Headers[ReplayedHeader] = "true";
                }
                // The stored body is returned byte for byte, so a replay is identical to the original response.
                return new ContentResult { StatusCode = StatusCodes.Status201Created, ContentType = "application/json", Content = result.Body };
            case SubmitOutcome.KeyMissing:
                return Problems.Of(ProblemTypes.IdempotencyKeyMissing, StatusCodes.Status400BadRequest, "An Idempotency-Key header is required.");
            case SubmitOutcome.KeyInvalid:
                return Problems.Of(ProblemTypes.IdempotencyKeyInvalid, StatusCodes.Status400BadRequest,
                    "The Idempotency-Key must be 1 to 64 letters, digits or dashes.");
            case SubmitOutcome.KeyReused:
                return Problems.Of(ProblemTypes.IdempotencyKeyReused, StatusCodes.Status422UnprocessableEntity,
                    "This Idempotency-Key was already used with a different request.");
            case SubmitOutcome.Invalid:
                return Problems.Validation(result.Errors!);
            case SubmitOutcome.PromotionNotApplied:
                return Problems.WithQuote(Problems.Validation(result.Errors!), ProblemTypes.PromotionNotApplied,
                    "A promo code can't be applied to this order.", result.CurrentQuote!);
            case SubmitOutcome.PriceChanged:
                return Problems.WithQuote(Problems.Of(ProblemTypes.PriceChanged, StatusCodes.Status409Conflict,
                    "The price changed since you reviewed the order."), ProblemTypes.PriceChanged,
                    "The price changed since you reviewed the order.", result.CurrentQuote!);
            default:
                throw new InvalidOperationException($"Unhandled submit outcome {result.Outcome}.");
        }
    }

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }
        Response.Headers.ETag = OrderService.ETag(order.Version);
        return Ok(OrderView.ToResponse(order, await orders.ZoneAsync(order, cancellationToken)));
    }

    [HttpGet("{orderId:guid}/status")]
    [ProducesResponseType<OrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }
        Response.Headers.ETag = OrderService.ETag(order.Version);
        return Ok(OrderView.ToStatus(order, await orders.ZoneAsync(order, cancellationToken)));
    }

    [HttpGet("{orderId:guid}/description")]
    [Produces("text/plain")]
    public async Task<IActionResult> Description(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        return order is null ? NotFound() : Content(OrderView.DescriptionText(order), "text/plain");
    }

    private new static ObjectResult NotFound() =>
        Problems.Of(ProblemTypes.OrderNotFound, StatusCodes.Status404NotFound, "Order not found.");
}
