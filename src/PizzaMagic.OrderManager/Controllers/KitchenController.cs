using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Domain.Ordering;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Controllers;

/// <summary>
/// The restaurant side (plan section 5.4): lists the active orders and moves an order to its next status. No auth, per
/// the spec; in production it would sit behind restaurant authentication.
/// </summary>
[ApiController]
[Route("api/v1/kitchen/orders")]
public sealed class KitchenController(OrderService orders) : ControllerBase
{
    /// <summary>The restaurant's active orders, oldest first (default restaurant when <paramref name="restaurantId"/> is omitted).</summary>
    [HttpGet]
    [ProducesResponseType<KitchenQueueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Queue([FromQuery] long? restaurantId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await orders.KitchenQueueAsync(restaurantId, cancellationToken));
        }
        catch (RestaurantNotFoundException)
        {
            return Problems.Of(ProblemTypes.RestaurantNotFound, StatusCodes.Status404NotFound, "Restaurant not found.",
                $"Restaurant {restaurantId} doesn't exist.");
        }
    }


    [HttpPost("{orderId:guid}/advance")]
    [ProducesResponseType<AdvanceOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Advance(Guid orderId, [FromBody] AdvanceOrderRequest request, CancellationToken cancellationToken) =>
        this.ToHttp(await orders.AdvanceAsync(orderId, request, OrderActor.Kitchen, cancellationToken), StatusCodes.Status200OK);
}
