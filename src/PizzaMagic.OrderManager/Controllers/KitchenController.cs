using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Controllers;

/// <summary>
/// The restaurant side (plan section 5.4): moves an order to its next status. No auth, per the spec; in production it
/// would sit behind restaurant authentication.
/// </summary>
[ApiController]
[Route("api/v1/kitchen/orders")]
public sealed class KitchenController(OrderService orders) : ControllerBase
{
    [HttpPost("{orderId:guid}/advance")]
    [ProducesResponseType<AdvanceOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Advance(Guid orderId, [FromBody] AdvanceOrderRequest request, CancellationToken cancellationToken) =>
        this.ToHttp(await orders.AdvanceAsync(orderId, request, OrderActor.Kitchen, cancellationToken), StatusCodes.Status200OK);
}
