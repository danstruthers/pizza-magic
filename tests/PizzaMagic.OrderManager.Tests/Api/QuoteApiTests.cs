using System.Net;
using System.Net.Http.Json;
using System.Text;
using PizzaMagic.Contracts.Api;

namespace PizzaMagic.OrderManager.Tests.Api;

public class QuoteApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static LineItemRequest Pizza(string size, int quantity, params string[] toppings) =>
        new("Pizza", size, quantity, toppings.Select(t => new ToppingRequest(t)).ToList());

    private static QuoteRequest P9(params string[] codes) =>
        new(null, null, [Pizza("LARGE", 2, "PEPPERONI", "BACON")], codes.Length == 0 ? ["ILOVEPIZZA"] : codes, new TipRequest("Percent", "15"));

    private Task<HttpResponseMessage> PostAsync(object body) =>
        factory.CreateClient().PostAsJsonAsync("/api/v1/quotes", body, ApiFactory.Json);

    private async Task<QuoteResponse> QuoteAsync(QuoteRequest request)
    {
        var response = await PostAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<QuoteResponse>();
    }

    // P9: 2 x Large + pepperoni + bacon, ILOVEPIZZA, 15% tip = $31.48.
    [Fact]
    public async Task POST_quote_matches_golden_P9()
    {
        var quote = await QuoteAsync(P9());

        (quote.SubtotalCents, quote.DiscountCents, quote.TaxableCents, quote.TaxCents, quote.BillCents, quote.TotalCents)
            .ShouldBe((2856L, 286L, 2570L, 167L, 2737L, 3148L));
        quote.Tip.ShouldBe(new TipResponse("Percent", "15", 150_000, 411, null));
        quote.CanSubmit.ShouldBeTrue();
        quote.RestaurantId.ShouldBe(1);
        quote.Channel.ShouldBe("ONLINE");

        var line = quote.LineItems.Single();
        (line.Index, line.LineNumber, line.Kind, line.Quantity, line.UnitPriceCents, line.LineTotalCents, line.DiscountCents)
            .ShouldBe((0, 1, "Pizza", 2, 1428L, 2856L, 286L));
        line.Size.ShouldBe(new PricedProductResponse("LARGE", "Large", 1200, "Global"));
        line.Crust.ShouldBe(new PricedChoiceResponse("HAND_TOSSED", "Hand tossed", 0, true));
        line.Toppings.Select(t => (t.Code, t.Tier, t.Portion, t.Multiplier, t.PriceSource, t.ToppingUnitPriceCents, t.PlacementChargeCents))
            .ShouldBe([("PEPPERONI", "BASIC", "WHOLE", 1, "GlobalTier", 96L, 96L), ("BACON", "PREMIUM", "WHOLE", 1, "GlobalTier", 132L, 132L)]);
        line.Description.ShouldBe("2 x Large pizza with Pepperoni, Bacon");

        var promo = quote.Promotions.Single();
        (promo.Code, promo.Status, promo.DiscountCents, promo.Message).ShouldBe(("ILOVEPIZZA", "Applied", 286L, "ILOVEPIZZA saves $2.86."));
        promo.Allocations.Sum(a => a.AmountCents).ShouldBe(286);

        quote.TaxRounding.ShouldBe(new TaxRoundingResponse("HALF_UP", "COMBINED"));
        quote.TaxLines.ShouldBe([new TaxLineResponse("WA", 0, "PREPARED_FOOD", 2856, 286, 2570, 65_000, "Default", 167)]);
        quote.Description.ShouldBe(
            "2 x Large pizza with Pepperoni, Bacon — $28.56\nPromo ILOVEPIZZA: -$2.86\nTax: $1.67\nTip (15%): $4.11\nTotal: $31.48");
    }

    // P24: the later conflicting code is Rejected (UQ3), dropped from the normalized draft; the total is unchanged.
    [Fact]
    public async Task Quote_conflict_returns_200_second_code_Rejected_and_normalized_codes()
    {
        var quote = await QuoteAsync(P9("ILOVEPIZZA", "SAVE5"));

        var save5 = quote.Promotions[1];
        (save5.Code, save5.Status, save5.DiscountCents).ShouldBe(("SAVE5", "Rejected", 0L));
        save5.ConflictsWith.ShouldBe(["ILOVEPIZZA"]);
        save5.Message.ShouldBe("SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5.");
        quote.NormalizedPromoCodes.ShouldBe(["ILOVEPIZZA"]);
        quote.NormalizedDraft.PromoCodes.ShouldBe(["ILOVEPIZZA"]);
        quote.TotalCents.ShouldBe(3148);
        quote.CanSubmit.ShouldBeTrue();
    }

    // PR17: a malformed code never fails the quote.
    [Fact]
    public async Task Quote_bad_promo_format_is_200_InvalidFormat()
    {
        var quote = await QuoteAsync(P9("I LOVE PIZZA"));

        quote.Promotions.Single().Status.ShouldBe("InvalidFormat");
        quote.DiscountCents.ShouldBe(0);
        quote.NormalizedPromoCodes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Quote_field_errors_422_with_paths()
    {
        var request = new QuoteRequest(null, null,
            [new LineItemRequest("Pizza", "LARGE", 0, [new ToppingRequest("PEPPERONI", "WHOLE", 2), new ToppingRequest("PEPPERONI", "LEFT", 2)])],
            [], new TipRequest("Amount", "1.234"));

        var response = await PostAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("type").GetString().ShouldBe(ProblemTypes.Validation);
        problem.GetProperty("title").GetString().ShouldBe("The order has invalid fields.");
        problem.FieldErrors().ShouldBe(
        [
            ("lineItems[0].quantity", "QUANTITY_OUT_OF_RANGE"),
            ("lineItems[0].toppings[0]", "PLACEMENT_NOT_SUPPORTED"),
            ("lineItems[0].toppings[1]", "PLACEMENT_NOT_SUPPORTED"),
            ("tip.value", "TIP_INVALID"),
        ], ignoreOrder: true);
        problem.GetProperty("errors").GetProperty("lineItems[0].quantity")[0].GetProperty("message").GetString()
            .ShouldBe("Quantity must be between 1 and 20.");
    }

    // A23 / P23.
    [Fact]
    public async Task Quote_empty_cart_200_ORDER_EMPTY()
    {
        var quote = await QuoteAsync(new QuoteRequest(null, null, [], null, null));

        quote.TotalCents.ShouldBe(0);
        quote.Issues.Select(i => i.Code).ShouldBe(["ORDER_EMPTY"]);
        quote.CanSubmit.ShouldBeFalse();
        (await QuoteAsync(new QuoteRequest(null, null, null, null, null))).Issues.Select(i => i.Code).ShouldBe(["ORDER_EMPTY"]);
    }

    // P13 and N7: the normalized draft names the defaults and keeps a NotEligible code, so canSubmit is false.
    [Fact]
    public async Task Quote_returns_normalized_draft_and_canSubmit()
    {
        var quote = await QuoteAsync(new QuoteRequest(null, null, [Pizza("small", 4)], ["save5"], null));

        quote.TotalCents.ShouldBe(2130);
        quote.CanSubmit.ShouldBeFalse();
        quote.Promotions.Single().Message.ShouldBe("SAVE5 requires an order subtotal over $20.00 (yours is $20.00).");
        quote.NormalizedDraft.Channel.ShouldBe("ONLINE");
        quote.NormalizedDraft.PromoCodes.ShouldBe(["SAVE5"]);
        quote.NormalizedDraft.Tip.ShouldBeNull();
        var line = quote.NormalizedDraft.LineItems.Single();
        (line.Kind, line.SizeCode, line.Quantity, line.CrustCode, line.SauceCode).ShouldBe(("Pizza", "SMALL", 4, "HAND_TOSSED", "CLASSIC_TOMATO"));
    }

    // UQ22: a dollar tip reports its effective rate of the bill, shown with two decimals.
    [Fact]
    public async Task Dollar_tip_reports_effective_rate()
    {
        var quote = await QuoteAsync(new QuoteRequest(null, null, [Pizza("LARGE", 2, "PEPPERONI", "BACON")], ["ILOVEPIZZA"], new TipRequest("Amount", "3.00")));

        quote.Tip.ShouldBe(new TipResponse("Amount", "3.00", null, 300, 1096));
        quote.TotalCents.ShouldBe(3037);
        quote.Description.ShouldContain("Tip: $3.00 (10.96%)");
    }

    [Fact]
    public async Task Add_on_lines_are_not_built()
    {
        var response = await PostAsync(new QuoteRequest(null, null, [new LineItemRequest("AddOn", null, 1, null, ProductCode: "GARLIC_KNOTS")], null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadJsonAsync()).FieldErrors().ShouldBe([("lineItems[0].kind", "LINE_KIND_NOT_SUPPORTED")]);
    }

    [Fact]
    public async Task Unknown_channel_and_restaurant_are_field_errors()
    {
        var response = await PostAsync(new QuoteRequest(99, "SMOKE_SIGNAL", [Pizza("SMALL", 1)], null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadJsonAsync()).FieldErrors().ShouldBe(
            [("restaurantId", "UNKNOWN_RESTAURANT"), ("channel", "UNKNOWN_CHANNEL")], ignoreOrder: true);
    }

    [Fact]
    public async Task Malformed_json_400()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/quotes", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("type").GetString().ShouldBe(ProblemTypes.Validation);
    }

    [Fact]
    public async Task Unknown_property_400()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/quotes",
            new StringContent("""{ "lineItems": [], "bogus": 1 }""", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
