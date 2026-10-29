using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Models;

namespace PizzaMagic.Web.Controllers;

/// <summary>
/// The simulated restaurant side (spec requirement 5: "simulate this however you like"): the active orders, oldest
/// first, each with one button for its next step. The button sends the status the order had when the page was drawn,
/// so a stale page or a double click can't skip a step. No auth, like the order service's kitchen endpoint.
/// </summary>
public sealed class KitchenController(IOrderManagerClient orderManager, IConfiguration configuration) : Controller
{
    private const string NoticeKey = "KitchenNotice";

    [HttpGet("/Kitchen")]
    public async Task<IActionResult> Index()
    {
        var result = await orderManager.GetKitchenQueueAsync(ConfiguredRestaurant.Id(configuration), HttpContext.RequestAborted);
        return View(new KitchenViewModel(
            result.IsSuccess ? result.Value!.Orders : null,
            TempData[NoticeKey] as string,
            result.Kind switch
            {
                ApiResultKind.Success => null,
                ApiResultKind.Problem when result.Problem!.Type == ProblemTypes.RestaurantNotFound =>
                    "This kitchen's restaurant doesn't exist. Check OrderManager:RestaurantId.",
                _ => "Can't reach the order service right now. This page will try again shortly.",
            }));
    }

    [HttpPost("/Kitchen/{id:guid}/Advance")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Advance(Guid id, [FromForm] string? expectedStatus)
    {
        var shortId = id.ToString()[..8];
        if (string.IsNullOrWhiteSpace(expectedStatus))
        {
            return BadRequest();
        }

        var result = await orderManager.AdvanceAsync(id, expectedStatus, HttpContext.RequestAborted);
        TempData[NoticeKey] = result.Kind switch
        {
            ApiResultKind.Success => $"Order {shortId} is now {Display.StatusName(result.Value!.Status)}.",
            ApiResultKind.Problem when result.Problem!.Type == ProblemTypes.OrderNotFound => $"Order {shortId} no longer exists.",
            ApiResultKind.Problem when result.Problem!.Extension("currentStatus") is { } current =>
                $"Order {shortId} had already changed: it is now {Display.StatusName(current.GetString() ?? "")}.",
            ApiResultKind.Problem => result.Problem!.Detail ?? $"Order {shortId} couldn't be moved on.",
            _ => $"Couldn't reach the order service, so order {shortId} wasn't moved on. Try again.",
        };
        return Redirect("/Kitchen");
    }
}
