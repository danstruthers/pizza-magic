using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Models;
using PizzaMagic.Web.Models.Orders;
using PizzaMagic.Web.Models.Session;

namespace PizzaMagic.Web.Controllers;

/// <summary>Order details, status polling, edit, cancel, recent orders and lookup (plan sections 6.1 and 6.4).</summary>
public sealed class OrdersController(IOrderManagerClient orderManager, RecentOrdersCookie recentOrders) : Controller
{
    private const string NotFoundText = "We couldn't find that order. Check the order number and try again.";
    private const string UnavailableText = "We can't reach the restaurant right now. Please try again in a moment.";
    private const string Cancelled = "Your order was cancelled.";
    private const string CancelUnconfirmed = "We couldn't confirm the cancellation. Check the status below and try again.";
    private const string InvalidNumber = "That isn't a valid order number.";

    [HttpGet("/Orders/{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var result = await orderManager.GetOrderAsync(id, HttpContext.RequestAborted);
        if (result.IsSuccess)
        {
            return View(new OrderDetailsViewModel(result.Value!, TempData[CheckoutController.NoticeKey] as string));
        }
        return result.Problem?.Type == ProblemTypes.OrderNotFound
            ? Message(StatusCodes.Status404NotFound, "Order not found", "We couldn't find that order.")
            : Message(StatusCodes.Status503ServiceUnavailable, "Order unavailable", UnavailableText);
    }

    /// <summary>The polling endpoint for the details page: the service's status JSON, passed through.</summary>
    [HttpGet("/Orders/{id:guid}/Status")]
    public async Task<IActionResult> Status(Guid id)
    {
        var result = await orderManager.GetStatusAsync(id, HttpContext.RequestAborted);
        return result.Kind switch
        {
            ApiResultKind.Success => Ok(result.Value),
            ApiResultKind.Problem => StatusCode(result.Problem!.Status, new { type = result.Problem.Type }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = UnavailableText }),
        };
    }

    /// <summary>
    /// Loads the order into the cart in edit mode and opens the builder; Review and Place then update it (PUT with
    /// If-Match) instead of creating a new order.
    /// </summary>
    [HttpPost("/Orders/{id:guid}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id)
    {
        var cart = CartStore.Load(HttpContext.Session);
        if (cart.PendingTicket is not null)
        {
            return Redirect("/Checkout");   // an order is waiting for confirmation; never overwrite its retry
        }

        var result = await orderManager.GetOrderAsync(id, HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            TempData[CheckoutController.NoticeKey] = result.Problem?.Type == ProblemTypes.OrderNotFound ? NotFoundText : UnavailableText;
            return Redirect($"/Orders/{id}");
        }
        var order = result.Value!;
        if (!order.AllowedActions.Contains("update"))
        {
            TempData[CheckoutController.NoticeKey] = CheckoutController.KitchenStarted;
            return Redirect($"/Orders/{id}");
        }

        CartStore.Save(HttpContext.Session, cart with
        {
            Revision = cart.Revision + 1,   // newer than anything the builder posted before, so it isn't overwritten
            LineItems = order.Pricing.LineItems.Select(ToRequest).ToList(),
            PromoCodes = order.Pricing.Promotions.Select(p => p.Code).ToList(),
            Tip = order.Pricing.Tip.Mode == "None" ? null : new TipRequest(order.Pricing.Tip.Mode, order.Pricing.Tip.Input),
            EditingOrderId = id,
            EditingVersion = order.Version,
            EditingRestaurantId = order.RestaurantId,   // priced where the service will price the update
            EditingChannel = order.Channel,
        });
        return Redirect("/");
    }

    [HttpPost("/Orders/{id:guid}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, [FromForm] int? version)
    {
        // The version the page showed: an order changed since (another tab) is not cancelled blind.
        var result = await orderManager.CancelAsync(id, version, HttpContext.RequestAborted);
        TempData[CheckoutController.NoticeKey] = result.Kind switch
        {
            ApiResultKind.Success => Cancelled,
            ApiResultKind.Problem when result.Problem!.Type == ProblemTypes.OrderNotFound => NotFoundText,
            ApiResultKind.Problem when result.Problem!.Type == ProblemTypes.VersionMismatch => CheckoutController.ChangedElsewhere,
            ApiResultKind.Problem when result.Problem!.Status == StatusCodes.Status409Conflict => CheckoutController.KitchenStarted,
            ApiResultKind.Problem => result.Problem!.Detail ?? CancelUnconfirmed,
            _ => CancelUnconfirmed,
        };
        return Redirect($"/Orders/{id}");
    }

    [HttpGet("/Orders")]
    public async Task<IActionResult> Recent() => View(new RecentOrdersViewModel(await RecentAsync(), null));

    /// <summary>Opens an order by its number, e.g. one placed on another device.</summary>
    [HttpGet("/Orders/Lookup")]
    public async Task<IActionResult> Lookup(string? orderId)
    {
        if (Guid.TryParse(orderId?.Trim(), out var id))
        {
            return Redirect($"/Orders/{id}");
        }
        var message = string.IsNullOrWhiteSpace(orderId) ? null : InvalidNumber;
        return View(nameof(Recent), new RecentOrdersViewModel(await RecentAsync(), message));
    }

    /// <summary>
    /// The cookie's orders (at most 10) read from the service in parallel, newest first. An order that can't be read is
    /// still listed, with a short reason, so one failure never hides the others.
    /// </summary>
    private async Task<IReadOnlyList<RecentOrderRow>> RecentAsync()
    {
        // The Session's last placed order too: if the response that placed it was lost, so was its cookie.
        var ids = recentOrders.Read(Request);
        if (CartStore.Load(HttpContext.Session).LastPlacedOrderId is { } last && !ids.Contains(last))
        {
            recentOrders.Add(HttpContext, last);
            ids = [last, .. ids];
        }
        var reads = ids.Select(async id =>
        {
            var result = await orderManager.GetOrderAsync(id, HttpContext.RequestAborted);
            return result.IsSuccess
                ? new RecentOrderRow(id, result.Value, null)
                : new RecentOrderRow(id, null, result.Problem?.Type == ProblemTypes.OrderNotFound ? "Order not found." : "Details unavailable right now.");
        });
        return await Task.WhenAll(reads);
    }

    private ViewResult Message(int status, string title, string text)
    {
        Response.StatusCode = status;
        return View("Message", new MessageViewModel(title, text));
    }

    private static LineItemRequest ToRequest(QuoteLineResponse line) => new(
        line.Kind,
        line.Size.Code,
        line.Quantity,
        line.Toppings.Select(t => new ToppingRequest(t.Code, t.Portion, t.Multiplier)).ToList(),
        line.Crust.Code,
        line.Sauce.Code);
}
