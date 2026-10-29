using System.Net;
using System.Text.Json;
using PizzaMagic.Contracts.Api;
using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Tests.Builder;

namespace PizzaMagic.Web.Tests.Checkout;

/// <summary>Review and Place (plan sections 6.2 and 6.4): what Review shows is exactly what Place submits.</summary>
public class CheckoutTests
{
    private static ApiResult<QuoteResponse> Priced(long total, IReadOnlyList<string>? codes = null) =>
        ApiResult<QuoteResponse>.Success(Quotes.Simple(total, normalizedCodes: codes, normalizedLines: [Quotes.NormalizedLarge]), null, false);

    /// <summary>
    /// A browser with one pizza in its Session cart. The quote proxy saves the draft even when the quote itself fails
    /// (some tests rely on that), so only a refused post (400) is an error here.
    /// </summary>
    private static async Task<HttpClient> CartAsync(WebFactory factory, long revision = 1, string size = "LARGE", params string[] codes)
    {
        var (client, token) = await factory.BrowserAsync();
        (await WebFactory.PostQuoteAsync(client, token, WebFactory.Draft(revision, size, codes))).StatusCode.ShouldNotBe(HttpStatusCode.BadRequest);
        return client;
    }

    private static string Serialized<T>(T value) => JsonSerializer.Serialize(value, WebFactory.Json);

    [Fact]
    public async Task Place_success_redirects_and_clears_cart()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);
        review.Html.ShouldContain("Place order — $13.80");
        review.Html.ShouldContain("Total: 1380");               // the full description (Req. 3)

        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe($"/Orders/{SampleOrders.Id}");
        WebFactory.RenderedCart(await client.GetStringAsync("/")).GetProperty("lineItems").GetArrayLength().ShouldBe(0);
        (await client.GetStringAsync("/Orders")).ShouldContain(SampleOrders.Id.ToString());   // recent-orders cookie
    }

    // Seen = charged: an edit made in another tab after Review never reaches the order.
    [Fact]
    public async Task Place_submits_ticket_draft_not_session_cart()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);
        (await WebFactory.PostQuoteAsync(client, review.Token, WebFactory.Draft(2, "SMALL"))).EnsureSuccessStatusCode();

        await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        var (request, _) = factory.OrderManager.SubmitRequests.ShouldHaveSingleItem();
        Serialized(request.LineItems).ShouldBe(Serialized(new[] { Quotes.NormalizedLarge }));
        request.ExpectedTotalCents.ShouldBe(1380);
    }

    // The Session cart can hold input the server will normalize: a rejected conflicting code, an omitted crust.
    // Here the builder's last quote failed, so the cart still has both codes and no crust or sauce.
    [Fact]
    public async Task Review_ticket_uses_normalized_codes()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Unavailable("timeout");
        var client = await CartAsync(factory, codes: ["ILOVEPIZZA", "SAVE5"]);
        var rejected = new PromotionOutcomeResponse("SAVE5", "Rejected", 0,
            "SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5.", ["ILOVEPIZZA"], []);
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(
            Quotes.Simple(1242, normalizedCodes: ["ILOVEPIZZA"], normalizedLines: [Quotes.NormalizedLarge], promotions: [rejected]), null, false);

        var review = await WebFactory.ReviewAsync(client);
        review.Html.ShouldContain("SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5.");
        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var (request, _) = factory.OrderManager.SubmitRequests.ShouldHaveSingleItem();
        request.PromoCodes.ShouldBe(["ILOVEPIZZA"]);
        request.LineItems!.Single().CrustCode.ShouldBe("HAND_TOSSED");
        request.LineItems!.Single().SauceCode.ShouldBe("CLASSIC_TOMATO");
    }

    [Fact]
    public async Task Review_writes_the_normalized_draft_back_to_the_cart()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Unavailable("timeout");
        var client = await CartAsync(factory, codes: ["ilovepizza", "SAVE5"]);
        factory.OrderManager.Quote = _ => Priced(1242, ["ILOVEPIZZA"]);

        await WebFactory.ReviewAsync(client);

        var cart = WebFactory.RenderedCart(await client.GetStringAsync("/"));
        cart.GetProperty("promoCodes").EnumerateArray().Select(c => c.GetString()).ShouldBe(["ILOVEPIZZA"]);
        cart.GetProperty("lineItems")[0].GetProperty("crustCode").GetString().ShouldBe("HAND_TOSSED");
    }

    [Fact]
    public async Task Review_disabled_when_canSubmit_false()
    {
        await using var factory = new WebFactory();
        var notEligible = new PromotionOutcomeResponse("SAVE5", "NotEligible", 0, "SAVE5 needs an order over $20.00.", [], []);
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Success(Quotes.Simple(2000, canSubmit: false,
            normalizedCodes: ["SAVE5"], normalizedLines: [Quotes.NormalizedLarge], promotions: [notEligible],
            issues: [new IssueResponse("PROMO_NOT_ELIGIBLE", "Remove SAVE5 or add $0.01 more to use it.")]), null, false);
        var client = await CartAsync(factory, codes: ["SAVE5"]);

        var review = await WebFactory.ReviewAsync(client);

        review.Ticket.ShouldBeNull();
        review.Html.ShouldContain("Remove SAVE5 or add $0.01 more to use it.");
        review.Html.ShouldContain("SAVE5 needs an order over $20.00.");
        review.Html.ShouldContain("Back to builder");
        review.Html.ShouldNotContain("Place order");
    }

    [Fact]
    public async Task Tampered_ticket_rejected()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);
        var ticket = review.Ticket!;
        var tampered = ticket[..^6] + (ticket[^6] == 'A' ? 'B' : 'A') + ticket[^5..];

        var altered = await WebFactory.PlaceAsync(client, review.Token, tampered);
        var invented = await WebFactory.PlaceAsync(client, review.Token, "not-a-ticket");

        altered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        invented.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        WebFactory.Text(await altered.Content.ReadAsStringAsync()).ShouldContain("This order page has expired or was changed. Please review your order again.");
        factory.OrderManager.SubmitRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Place_requires_antiforgery()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);

        var response = await WebFactory.PlaceAsync(client, "not-a-token", review.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.OrderManager.SubmitRequests.ShouldBeEmpty();
    }

    // Outage after retries: the order may or may not exist. Keep the ticket and key, lock the cart, offer a safe retry.
    [Fact]
    public async Task Place_unavailable_keeps_ticket_and_locks_cart()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        factory.OrderManager.Submit = (_, _) => ApiResult<OrderResponse>.Unavailable("timeout");
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);

        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        var html = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        WebFactory.Text(html).ShouldContain("We couldn't confirm your order. Your cart is saved.");
        WebFactory.Text(html).ShouldContain("Try again");
        WebFactory.Ticket(html).ShouldBe(review.Ticket);

        var builder = await client.GetStringAsync("/");
        WebFactory.Text(builder).ShouldContain("We couldn't confirm your order");
        (await WebFactory.PostQuoteAsync(client, WebFactory.Token(builder), WebFactory.Draft(5, "SMALL"))).EnsureSuccessStatusCode();
        var cart = WebFactory.RenderedCart(await client.GetStringAsync("/"));
        cart.GetProperty("revision").GetInt64().ShouldBe(1);   // locked: the newer draft was quoted but not saved
        cart.GetProperty("lineItems")[0].GetProperty("sizeCode").GetString().ShouldBe("LARGE");

        // Review shows the pending order again (with Try again), not a fresh quote of the cart.
        var pending = await WebFactory.ReviewAsync(client);
        pending.Ticket.ShouldBe(review.Ticket);
        pending.Html.ShouldContain("We couldn't confirm your order");
    }

    [Fact]
    public async Task Try_again_reuses_key()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var outcomes = new Queue<ApiResult<OrderResponse>>([
            ApiResult<OrderResponse>.Unavailable("timeout"),
            ApiResult<OrderResponse>.Success(SampleOrders.Order(), "\"1\"", true),
        ]);
        factory.OrderManager.Submit = (_, _) => outcomes.Dequeue();
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);

        var first = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);
        var retryTicket = WebFactory.Ticket(await first.Content.ReadAsStringAsync())!;
        var second = await WebFactory.PlaceAsync(client, review.Token, retryTicket);

        second.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var submits = factory.OrderManager.SubmitRequests;
        submits.Count.ShouldBe(2);
        submits[1].Key.ShouldBe(submits[0].Key);
        Serialized(submits[1].Request).ShouldBe(Serialized(submits[0].Request));
        WebFactory.Text(await client.GetStringAsync("/")).ShouldNotContain("We couldn't confirm your order");
    }

    // The key is reused while the cart's content is unchanged (so a re-review can't create a second order), and
    // rotated once it changes.
    [Fact]
    public async Task Key_reused_until_content_changes()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        // A definitive refusal each time (refusals are never stored by the service), so no Place leaves the cart locked.
        factory.OrderManager.Submit = (_, _) => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.Validation, 422));
        var client = await CartAsync(factory);
        var first = await WebFactory.ReviewAsync(client);
        var again = await WebFactory.ReviewAsync(client);
        (await WebFactory.PostQuoteAsync(client, first.Token, WebFactory.Draft(2, "MEDIUM"))).EnsureSuccessStatusCode();
        var changed = await WebFactory.ReviewAsync(client);

        foreach (var ticket in new[] { first.Ticket!, again.Ticket!, changed.Ticket! })
        {
            await WebFactory.PlaceAsync(client, first.Token, ticket);
        }

        var keys = factory.OrderManager.SubmitRequests.Select(s => s.Key).ToList();
        keys[1].ShouldBe(keys[0]);
        keys[2].ShouldNotBe(keys[0]);
    }

    [Fact]
    public async Task Place_price_changed_shows_new_total()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var current = Quotes.Simple(1500, normalizedLines: [Quotes.NormalizedLarge]);
        var outcomes = new Queue<ApiResult<OrderResponse>>([
            ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.PriceChanged, 409, extensions: new { currentQuote = current })),
            ApiResult<OrderResponse>.Success(SampleOrders.Order(), "\"1\"", false),
        ]);
        factory.OrderManager.Submit = (_, _) => outcomes.Dequeue();
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);

        var changed = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);
        var html = await changed.Content.ReadAsStringAsync();

        changed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        WebFactory.Text(html).ShouldContain("Prices changed — please confirm the new total.");
        WebFactory.Text(html).ShouldContain("Place order — $15.00");
        var confirmed = await WebFactory.PlaceAsync(client, review.Token, WebFactory.Ticket(html)!);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var submits = factory.OrderManager.SubmitRequests;
        submits[1].Request.ExpectedTotalCents.ShouldBe(1500);
        submits[1].Key.ShouldBe(submits[0].Key);
    }

    [Fact]
    public async Task Place_422_maps_errors()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var errors = new Dictionary<string, IReadOnlyList<FieldErrorResponse>>
        {
            ["lineItems[0].sizeCode"] = [new FieldErrorResponse("PRODUCT_UNAVAILABLE", "Large is not available here.")],
        };
        factory.OrderManager.Submit = (_, _) => ApiResult<OrderResponse>.Failed(Problems.Of(ProblemTypes.Validation, 422, errors: errors));
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);

        var response = await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var html = WebFactory.Text(await response.Content.ReadAsStringAsync());
        html.ShouldContain("Pizza 1: Large is not available here.");
        html.ShouldContain("Back to builder");
        WebFactory.Ticket(html).ShouldBeNull();
        WebFactory.Text(await client.GetStringAsync("/")).ShouldNotContain("We couldn't confirm your order");   // not locked
    }

    // Reaching Review with nothing in the cart (a bookmark, the keyboard, a stale page) goes back to the builder.
    [Fact]
    public async Task Review_with_an_empty_cart_returns_to_the_builder()
    {
        await using var factory = new WebFactory();
        var (client, _) = await factory.BrowserAsync();

        var response = await client.GetAsync("/Checkout");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/");
        factory.OrderManager.QuoteRequests.ShouldBeEmpty();
    }

    // Code review: with the cart locked on one ticket (an outage), placing another ticket (an older Review page in
    // another tab) used to replace the lock and could create a second order nobody would see. The lock wins.
    [Fact]
    public async Task Another_ticket_while_locked_shows_the_pending_order_instead()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        factory.OrderManager.Submit = (_, _) => ApiResult<OrderResponse>.Unavailable("timeout");
        var client = await CartAsync(factory);
        var older = await WebFactory.ReviewAsync(client);
        (await WebFactory.PostQuoteAsync(client, older.Token, WebFactory.Draft(2, "SMALL"))).StatusCode.ShouldNotBe(HttpStatusCode.BadRequest);
        var newer = await WebFactory.ReviewAsync(client);
        (await WebFactory.PlaceAsync(client, newer.Token, newer.Ticket!)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var response = await WebFactory.PlaceAsync(client, older.Token, older.Ticket!);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var html = await response.Content.ReadAsStringAsync();
        WebFactory.Text(html).ShouldContain("Your earlier order is still waiting for confirmation.");
        WebFactory.Ticket(html).ShouldBe(newer.Ticket);
        factory.OrderManager.SubmitRequests.Count.ShouldBe(1);
    }

    // The lock is saved before the order service is called, and the call does not depend on the browser staying
    // connected: a phone dropping mid-Place must not leave an unlocked cart while the order may exist.
    [Fact]
    public async Task Lock_is_saved_before_the_order_service_is_called()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        factory.OrderManager.Submit = (_, _) => throw new OperationCanceledException("connection dropped");
        var client = await CartAsync(factory);
        var review = await WebFactory.ReviewAsync(client);

        await WebFactory.PlaceAsync(client, review.Token, review.Ticket!);

        factory.OrderManager.WriteTokens.ShouldHaveSingleItem().CanBeCanceled.ShouldBeFalse();
        WebFactory.Text(await client.GetStringAsync("/")).ShouldContain("We couldn't confirm your order");
    }

    // If the redirect after Place never reaches the phone, the recent-orders cookie is lost with it. The Session still
    // knows the order, so the builder and "Your orders" both show it.
    [Fact]
    public async Task Placed_order_stays_findable_when_the_response_is_lost()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var jar = new CookieJar();
        var client = factory.CreateDefaultClient(jar);
        var token = WebFactory.Token(await client.GetStringAsync("/"));
        (await WebFactory.PostQuoteAsync(client, token, WebFactory.Draft(1, "LARGE"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var review = await WebFactory.ReviewAsync(client);
        (await WebFactory.PlaceAsync(client, review.Token, review.Ticket!)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        jar.Remove("pm_recent");

        var builder = await client.GetStringAsync("/");
        var recent = await client.GetStringAsync("/Orders");

        WebFactory.Text(builder).ShouldContain("Your order 0f8fad5b was placed.");
        builder.ShouldContain($"href=\"/Orders/{SampleOrders.Id}\"");
        recent.ShouldContain(SampleOrders.Id.ToString());
    }

    [Fact]
    public async Task Zero_discount_is_not_shown()
    {
        await using var factory = new WebFactory();
        factory.OrderManager.Quote = _ => Priced(1380);
        var client = await CartAsync(factory);

        (await WebFactory.ReviewAsync(client)).Html.ShouldNotContain("Discounts");
    }

    /// <summary>A minimal browser cookie store that a test can drop a cookie from.</summary>
    private sealed class CookieJar : DelegatingHandler
    {
        private readonly Dictionary<string, string> _cookies = [];

        public void Remove(string name) => _cookies.Remove(name);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var pair in values.Select(v => v.Split(';')[0]))
                {
                    var at = pair.IndexOf('=');
                    var (name, value) = (pair[..at], pair[(at + 1)..]);
                    if (value.Length == 0) _cookies.Remove(name); else _cookies[name] = value;
                }
            }
            return response;
        }
    }

    [Fact]
    public async Task Review_says_so_when_the_service_is_unavailable()
    {
        await using var factory = new WebFactory();
        var client = await CartAsync(factory);
        factory.OrderManager.Quote = _ => ApiResult<QuoteResponse>.Unavailable("timeout");

        var review = await WebFactory.ReviewAsync(client);

        review.Ticket.ShouldBeNull();
        review.Html.ShouldContain("We can't price your order right now. Please try again in a moment.");
    }
}
