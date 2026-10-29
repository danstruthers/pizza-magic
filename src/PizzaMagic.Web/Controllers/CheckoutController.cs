using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Models;
using PizzaMagic.Web.Models.Checkout;
using PizzaMagic.Web.Models.Orders;
using PizzaMagic.Web.Models.Session;

namespace PizzaMagic.Web.Controllers;

/// <summary>
/// Review and Place (plan sections 6.1, 6.2 and 6.4). Review prices the Session cart afresh and seals the server's
/// normalized draft and total into a ticket; Place submits exactly that ticket, never the cart, so what the customer
/// saw is what they are charged.
/// </summary>
public sealed class CheckoutController(
    IOrderManagerClient orderManager,
    ReviewTicketProtector tickets,
    RecentOrdersCookie recentOrders,
    IConfiguration configuration) : Controller
{
    internal const string NoticeKey = "Notice";
    internal const string KitchenStarted = "The kitchen has already started your order, so it can't be changed or cancelled.";
    private const string PricesChanged = "Prices changed — please confirm the new total.";
    private const string TicketInvalid = "This order page has expired or was changed. Please review your order again.";
    private const string QuoteUnavailable = "We can't price your order right now. Please try again in a moment.";
    internal const string ChangedElsewhere = "This order was changed in another window. Here's the latest version.";
    private const string EarlierOrderPending = "Your earlier order is still waiting for confirmation. Try it again before placing another.";

    [HttpGet("/Checkout")]
    public async Task<IActionResult> Review()
    {
        var cart = CartStore.Load(HttpContext.Session);

        // An order is waiting for confirmation: show it again (with Try again), not a new quote of the cart.
        if (cart.PendingTicket is { } pending)
        {
            if (tickets.TryUnprotect(pending) is { } pendingTicket)
            {
                return View(Pending(pending, pendingTicket));
            }
            cart = cart with { PendingTicket = null };   // expired: nothing can be retried with it
        }

        // Nothing to review (a bookmark, a stale page): back to the builder rather than an empty Review page.
        if (cart.LineItems.Count == 0)
        {
            return Redirect("/");
        }

        // The key is reused while the cart's content is unchanged, so reviewing again can't create a second order.
        if (cart.KeyRevision != cart.Revision)
        {
            cart = cart with { SubmissionKey = Guid.NewGuid().ToString(), KeyRevision = cart.Revision };
        }

        var result = await orderManager.QuoteAsync(
            new QuoteRequest(cart.RestaurantId(configuration), cart.EditingChannel, cart.LineItems, cart.PromoCodes, cart.Tip),
            HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            CartStore.Save(HttpContext.Session, cart);
            return View(result.Problem is { } problem ? Refused(problem) : CheckoutViewModel.Message(QuoteUnavailable));
        }

        // What Review shows is what the builder shows: the normalized draft replaces the cart's content.
        var quote = result.Value!;
        cart = cart with { LineItems = quote.NormalizedDraft.LineItems, PromoCodes = quote.NormalizedDraft.PromoCodes, Tip = quote.NormalizedDraft.Tip };
        CartStore.Save(HttpContext.Session, cart);
        return View(Priced(quote, cart.SubmissionKey, cart.Revision, cart.EditingOrderId, cart.EditingVersion, banner: null));
    }

    [HttpPost("/Checkout/Place")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Place([FromForm] string? ticket)
    {
        if (tickets.TryUnprotect(ticket) is not { } review)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(nameof(Review), CheckoutViewModel.Message(TicketInvalid));
        }

        // An order already waiting for confirmation wins: placing another ticket (an older Review page) could create
        // a second order nobody sees. Only Try again (the same ticket and key) moves on.
        var cart = CartStore.Load(HttpContext.Session);
        if (cart.PendingTicket is { } pending && pending != ticket && tickets.TryUnprotect(pending) is { } pendingReview)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return View(nameof(Review), Pending(pending, pendingReview) with { Banner = EarlierOrderPending });
        }

        // Commit the lock before the order service hears of the order, so a dropped connection can't leave an unlocked
        // cart behind an order that may exist. The call isn't tied to the browser either (CancellationToken.None): it
        // runs to an answer or the client's timeout even if the customer has gone.
        cart = cart with { PendingTicket = ticket };
        CartStore.Save(HttpContext.Session, cart);
        await HttpContext.Session.CommitAsync();
        var draft = review.Draft;
        var result = review.OrderId is { } orderId
            ? await orderManager.UpdateAsync(orderId, review.Version!.Value,
                new UpdateOrderRequest(draft.LineItems, draft.PromoCodes, draft.Tip, review.ExpectedTotalCents, null),
                review.SubmissionKey, CancellationToken.None)
            : await orderManager.SubmitAsync(
                new SubmitOrderRequest(ConfiguredRestaurant.Id(configuration), draft.Channel, draft.LineItems, draft.PromoCodes, draft.Tip,
                    review.ExpectedTotalCents, null),
                review.SubmissionKey, CancellationToken.None);

        if (result.IsSuccess)
        {
            var placed = Guid.Parse(result.Value!.OrderId);
            CartStore.Save(HttpContext.Session, CartState.Empty(placed));
            recentOrders.Add(HttpContext, placed);
            return Redirect($"/Orders/{placed}");
        }
        if (result.Problem is not { } problem)
        {
            CartStore.Save(HttpContext.Session, cart);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(nameof(Review), Pending(ticket!, review));
        }

        cart = cart with { PendingTicket = null };
        switch (problem.Type)
        {
            case ProblemTypes.PriceChanged when CurrentQuote(problem) is { } current:
                CartStore.Save(HttpContext.Session, cart);
                Response.StatusCode = StatusCodes.Status409Conflict;
                return View(nameof(Review), Priced(current, review.SubmissionKey, review.Revision, review.OrderId, review.Version, PricesChanged));

            case ProblemTypes.VersionMismatch or ProblemTypes.OrderNotModifiable when review.OrderId is { } changed:
                // The edit can't be applied: leave edit mode and show the order as it is now.
                CartStore.Save(HttpContext.Session, CartState.Empty(cart.LastPlacedOrderId));
                TempData[NoticeKey] = problem.Type == ProblemTypes.VersionMismatch ? ChangedElsewhere : KitchenStarted;
                return Redirect($"/Orders/{changed}");

            default:
                CartStore.Save(HttpContext.Session, cart);
                Response.StatusCode = problem.Status;
                return View(nameof(Review), Refused(problem));
        }
    }

    private CheckoutViewModel Priced(QuoteResponse quote, string key, long revision, Guid? orderId, int? version, string? banner)
    {
        var ticket = quote.CanSubmit
            ? tickets.Protect(new ReviewTicket(quote.NormalizedDraft, quote.TotalCents, key, revision, orderId, version))
            : null;
        return new CheckoutViewModel(quote, ticket, orderId is not null, banner, quote.Issues.Select(i => i.Message).ToList());
    }

    private static CheckoutViewModel Pending(string ticket, ReviewTicket review) =>
        new(null, ticket, review.OrderId is not null, null, [], review.ExpectedTotalCents);

    private static CheckoutViewModel Refused(ApiProblem problem)
    {
        var messages = Display.FieldErrors(problem);
        return new CheckoutViewModel(null, null, false, null,
            messages.Count > 0 ? messages : [problem.Detail ?? problem.Title ?? "Your order couldn't be placed."]);
    }

    private static QuoteResponse? CurrentQuote(ApiProblem problem) =>
        problem.Extension("currentQuote") is { } json ? json.Deserialize<QuoteResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
}
