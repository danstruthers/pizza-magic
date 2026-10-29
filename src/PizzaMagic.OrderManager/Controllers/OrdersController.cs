using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Controllers;

/// <summary>Submit (idempotent), read, update (If-Match) and cancel orders.</summary>
[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        this.ToHttp(await orders.SubmitAsync(request, idempotencyKey, cancellationToken), StatusCodes.Status201Created, location: true);

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null)
        {
            return this.ToHttp(new OrderResult(OrderOutcome.NotFound), StatusCodes.Status200OK);
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
            return this.ToHttp(new OrderResult(OrderOutcome.NotFound), StatusCodes.Status200OK);
        }
        Response.Headers.ETag = OrderService.ETag(order.Version);
        return Ok(OrderView.ToStatus(order, await orders.ZoneAsync(order, cancellationToken)));
    }

    [HttpGet("{orderId:guid}/description")]
    [Produces("text/plain")]
    public async Task<IActionResult> Description(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        return order is null
            ? this.ToHttp(new OrderResult(OrderOutcome.NotFound), StatusCodes.Status200OK)
            : Content(OrderView.DescriptionText(order), "text/plain");
    }

    /// <summary>Replace the content while Not Started. If-Match is required; an Idempotency-Key makes a retry replay.</summary>
    [HttpPut("{orderId:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(
        Guid orderId,
        [FromBody] UpdateOrderRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        this.ToHttp(await orders.UpdateAsync(orderId, request, ifMatch, idempotencyKey, cancellationToken), StatusCodes.Status200OK);

    /// <summary>Cancel while Not Started; cancelling a cancelled order is an idempotent 200. If-Match is optional, enforced if sent.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid orderId, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken) =>
        this.ToHttp(await orders.CancelAsync(orderId, ifMatch, cancellationToken), StatusCodes.Status200OK);
}
