using PizzaMagic.Contracts.Api;
using PizzaMagic.OrderManager.Services;

namespace PizzaMagic.OrderManager.Tests;

public class RequestHasherTests
{
    private static SubmitOrderRequest Order(IReadOnlyList<ToppingRequest> toppings, string? name = "Dana", params string[] codes) =>
        new(null, null, [new LineItemRequest("Pizza", "LARGE", 2, toppings)], codes, new TipRequest("Percent", "15"), 3148, name);

    [Fact]
    public void Hash_is_sha256_hex_and_deterministic()
    {
        var hash = RequestHasher.HashCreate(Order([new("PEPPERONI")]));

        hash.Length.ShouldBe(64);
        hash.ShouldMatch("^[0-9a-f]{64}$");
        RequestHasher.HashCreate(Order([new("PEPPERONI")])).ShouldBe(hash);
    }

    // Syntactic normalization only: case, whitespace and omitted defaults do not change the hash.
    [Fact]
    public void Syntactic_variants_hash_the_same()
    {
        var plain = Order([new("PEPPERONI")], "Dana", "ILOVEPIZZA");
        var noisy = new SubmitOrderRequest(null, null,
            [new LineItemRequest("pizza", " large ", 2, [new ToppingRequest(" pepperoni ", "whole", 1)])],
            [" ilovepizza "], new TipRequest("Percent", "15"), 3148, "Dana");

        RequestHasher.HashCreate(noisy).ShouldBe(RequestHasher.HashCreate(plain));
    }

    // Request_hash_is_computed_before_catalog_load: the hash uses only the request, never catalog data, so two
    // bodies that differ only in how placements were written are different requests.
    [Fact]
    public void Request_hash_is_computed_before_catalog_load()
    {
        var twoLeftEntries = Order([new("PEPPERONI", "LEFT"), new("PEPPERONI", "LEFT")]);
        var leftTwice = Order([new("PEPPERONI", "LEFT", 2)]);

        RequestHasher.HashCreate(twoLeftEntries).ShouldNotBe(RequestHasher.HashCreate(leftTwice));
    }

    [Fact]
    public void Any_meaningful_change_changes_the_hash()
    {
        var baseline = RequestHasher.HashCreate(Order([new("PEPPERONI")]));

        RequestHasher.HashCreate(Order([new("PEPPERONI")], "Robin")).ShouldNotBe(baseline);
        RequestHasher.HashCreate(Order([new("PEPPERONI")]) with { ExpectedTotalCents = 3149 }).ShouldNotBe(baseline);
        RequestHasher.HashCreate(Order([new("BACON")])).ShouldNotBe(baseline);
        RequestHasher.HashCreate(Order([new("PEPPERONI")]) with { Tip = new TipRequest("Percent", "15.0") }).ShouldNotBe(baseline);
    }
}
