using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Models;
using PizzaMagic.Web.Models.Session;

namespace PizzaMagic.Web.Controllers;

/// <summary>
/// The order builder (plan section 6.1). The page is rendered from the service's menu, and every total it shows comes
/// from the quote proxy below: the builder never prices anything itself.
/// </summary>
public sealed class BuilderController(IOrderManagerClient orderManager, IConfiguration configuration) : Controller
{
    private const string UnavailableMessage = "Total unavailable — retrying";

    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var menu = await orderManager.GetMenuAsync(RestaurantId(), HttpContext.RequestAborted);
        var cart = CartStore.Load(HttpContext.Session);
        return View(new BuilderViewModel(menu.IsSuccess ? menu.Value : null, cart));
    }

    /// <summary>
    /// The running total: save the draft if it is newer than the Session cart, quote it, and replace the cart's promo
    /// list with the server's normalized codes (a rejected or malformed code leaves the cart).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quote([FromBody] BuilderDraft draft)
    {
        var cart = CartStore.Load(HttpContext.Session);
        // While an order waits for confirmation the cart is locked: drafts are still priced, never saved.
        var isNewest = cart.PendingTicket is null && draft.Revision > cart.Revision;
        if (isNewest)
        {
            cart = cart with
            {
                Revision = draft.Revision,
                LineItems = draft.LineItems ?? [],
                PromoCodes = draft.PromoCodes ?? [],
                Tip = draft.Tip,
            };
        }

        var result = await orderManager.QuoteAsync(
            new QuoteRequest(RestaurantId(), null, draft.LineItems ?? [], draft.PromoCodes ?? [], draft.Tip), HttpContext.RequestAborted);

        if (result.IsSuccess && isNewest)
        {
            cart = cart with { PromoCodes = result.Value!.NormalizedPromoCodes };
        }
        if (isNewest)
        {
            CartStore.Save(HttpContext.Session, cart);
        }

        return result.Kind switch
        {
            ApiResultKind.Success => Ok(result.Value),
            ApiResultKind.Problem => StatusCode(result.Problem!.Status, new { type = result.Problem.Type, errors = result.Problem.Errors }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = UnavailableMessage }),
        };
    }

    /// <summary>Empties the cart and leaves edit mode (not while an order is waiting for confirmation).</summary>
    [HttpPost("/Builder/StartOver")]
    [ValidateAntiForgeryToken]
    public IActionResult StartOver()
    {
        if (CartStore.Load(HttpContext.Session).PendingTicket is not null)
        {
            return Redirect("/Checkout");
        }
        CartStore.Save(HttpContext.Session, CartState.Empty());
        return Redirect("/");
    }

    private long? RestaurantId() => ConfiguredRestaurant.Id(configuration);
}
