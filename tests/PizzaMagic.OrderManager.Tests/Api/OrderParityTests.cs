using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PizzaMagic.Contracts.Api;
using static PizzaMagic.OrderManager.Tests.Api.OrderApiTests;

namespace PizzaMagic.OrderManager.Tests.Api;

/// <summary>
/// Seen = charged (spec requirement 2): for every submittable spec vector, the quote total, the submitted total and
/// the stored total are the same. P24 is submitted as its normalized draft; P13 and P23 are refused with no row.
/// </summary>
public class OrderParityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly LineItemRequest[] Golden =
    [
        Pizza("LARGE", 2, "PEPPERONI", "BACON"),
        Pizza("MEDIUM", 1, "EXTRA_CHEESE", "PINEAPPLE"),
        Pizza("SMALL", 3),
    ];

    private static TipRequest Percent(string value) => new("Percent", value);

    private static TipRequest Amount(string value) => new("Amount", value);

    public static TheoryData<string, QuoteRequest, long> Submittable => new()
    {
        { "P1", Q([Pizza("SMALL", 1)]), 533 },
        { "P2", Q([Pizza("MEDIUM", 1)]), 746 },
        { "P3", Q([Pizza("LARGE", 1)]), 1278 },
        { "P4", Q([Pizza("SMALL", 1), Pizza("SMALL", 1)]), 1065 },
        { "P5", Q(Golden), 5526 },
        { "P6", Q(Golden, Percent("18"), "ILOVEPIZZA"), 5869 },
        { "P7", Q(Golden, Amount("5.00"), "SAVE5"), 5494 },
        { "P8", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], null, "ILOVEPIZZA"), 2737 },
        { "P9", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], Percent("15"), "ILOVEPIZZA"), 3148 },
        { "P10", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], Amount("3.00"), "ILOVEPIZZA"), 3037 },
        { "P11", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], null, "SAVE5"), 2509 },
        { "P12", Q([Pizza("SMALL", 1, "BACON", "CHICKEN", "GARLIC")], null, "ILOVEPIZZA"), 637 },
        { "P14", Q([Pizza("SMALL", 1, "PEPPERONI"), Pizza("SMALL", 3)], null, "SAVE5"), 1640 },
        { "P15", Q([Pizza("LARGE", 1), Pizza("MEDIUM", 2)], null, "SAVE5"), 2237 },
        { "P16", Q([Pizza("MEDIUM", 3, "PEPPERONI", "BACON")], Amount("3.00"), "ILOVEPIZZA"), 2695 },
        { "P17", Q([Pizza("MEDIUM", 3)], null, "SAVE5"), 1704 },
        { "P19", Q([Pizza("SMALL", 1)], Percent("15")), 613 },
        { "P20", Q([Pizza("SMALL", 1)], Percent("20")), 640 },
        { "P21", Q([Pizza("SMALL", 1), Pizza("SMALL", 1)], Percent("10")), 1172 },
        { "P22", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], Percent("17.5")), 3574 },
        { "P24", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], Percent("15"), "ILOVEPIZZA", "SAVE5"), 3148 },
        { "P25", Q([Pizza("LARGE", 2, "PEPPERONI", "BACON")], null, "  ilovepizza "), 2737 },
    };

    private static QuoteRequest Q(LineItemRequest[] lines, TipRequest? tip = null, params string[] codes) => new(null, null, lines, codes, tip);

    [Theory]
    [MemberData(nameof(Submittable))]
    public async Task Quote_total_equals_submitted_total_equals_stored_total(string id, QuoteRequest request, long total)
    {
        var client = factory.CreateClient();

        var quote = await (await client.PostAsJsonAsync("/api/v1/quotes", request, ApiFactory.Json)).ReadAsync<QuoteResponse>();
        quote.TotalCents.ShouldBe(total, id);
        quote.CanSubmit.ShouldBeTrue(id);

        // Submit what the server said it will accept (as the order builder's review ticket does).
        var draft = quote.NormalizedDraft;
        var submit = new SubmitOrderRequest(null, draft.Channel, draft.LineItems, draft.PromoCodes, draft.Tip, quote.TotalCents, null);
        var response = await SubmitAsync(client, submit, Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, id + ": " + await response.Content.ReadAsStringAsync());
        var created = await response.ReadAsync<OrderResponse>();
        created.Pricing.TotalCents.ShouldBe(total, id);

        var stored = await (await client.GetAsync($"/api/v1/orders/{created.OrderId}")).ReadAsync<OrderResponse>();
        stored.Pricing.TotalCents.ShouldBe(total, id);
    }

    [Theory]
    [InlineData("P13")]
    [InlineData("P23")]
    public async Task Unsubmittable_vectors_are_refused_with_no_row(string id)
    {
        var request = id == "P13"
            ? new SubmitOrderRequest(null, null, [Pizza("SMALL", 4)], ["SAVE5"], null, 2130, null)
            : new SubmitOrderRequest(null, null, [], null, Percent("18"), 0, null);
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM customer_order";
        var before = (long)(await count.ExecuteScalarAsync())!;

        var response = await SubmitAsync(factory.CreateClient(), request, Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, id);
        ((long)(await count.ExecuteScalarAsync())!).ShouldBe(before);
    }
}
