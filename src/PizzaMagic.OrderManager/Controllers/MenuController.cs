using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Controllers;

/// <summary>GET /api/v1/menu: a restaurant's effective menu with server-computed prices.</summary>
[ApiController]
[Route("api/v1/menu")]
public sealed class MenuController(CatalogLoader loader) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MenuResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromQuery] long? restaurantId, CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await loader.LoadAsync(restaurantId, cancellationToken);
            if (catalog.Restaurant is { IsActive: false })
            {
                return NotFound(restaurantId);
            }
            return Ok(ResponseMapper.ToMenu(catalog));
        }
        catch (RestaurantNotFoundException)
        {
            return NotFound(restaurantId);
        }
    }

    private static ObjectResult NotFound(long? restaurantId) =>
        Problems.Of(ProblemTypes.RestaurantNotFound, StatusCodes.Status404NotFound, "Restaurant not found.",
            $"Restaurant {restaurantId} doesn't exist or isn't open.");
}
