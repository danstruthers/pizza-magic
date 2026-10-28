using PizzaMagic.Domain.Tax;

namespace PizzaMagic.Domain.Tests.Tax;

public class JurisdictionTests
{
    private static readonly JurisdictionNode Wa =
        new(1, "WA", "Washington State", ParentId: null, DefaultRatePpm: 65_000, TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined);

    // Resolver root case: a restaurant in the root jurisdiction has a chain of one.
    [Fact]
    public void Chain_of_a_root_jurisdiction_is_itself()
    {
        JurisdictionChainResolver.Resolve([Wa], 1).ShouldBe([Wa]);
    }

    [Fact]
    public void Chain_is_outermost_first()
    {
        var seattle = new JurisdictionNode(2, "SEATTLE", "Seattle", ParentId: 1, 38_500, null, null);

        JurisdictionChainResolver.Resolve([seattle, Wa], 2).Select(j => j.Code).ShouldBe(["WA", "SEATTLE"]);
    }

    [Fact]
    public void Missing_jurisdiction_or_parent_and_cycles_throw()
    {
        var orphan = new JurisdictionNode(3, "ORPHAN", "Orphan", ParentId: 99, 0, null, null);
        var a = new JurisdictionNode(4, "A", "A", ParentId: 5, 0, null, null);
        var b = new JurisdictionNode(5, "B", "B", ParentId: 4, 0, null, null);

        Should.Throw<InvalidOperationException>(() => JurisdictionChainResolver.Resolve([Wa], 42));
        Should.Throw<InvalidOperationException>(() => JurisdictionChainResolver.Resolve([Wa, orphan], 3));
        Should.Throw<InvalidOperationException>(() => JurisdictionChainResolver.Resolve([a, b], 4)).Message.ShouldContain("cycle");
    }

    [Fact]
    public void Chain_deeper_than_the_maximum_throws()
    {
        var nodes = Enumerable.Range(1, JurisdictionChainResolver.MaxJurisdictionDepth + 1)
            .Select(i => new JurisdictionNode(i, $"J{i}", $"J{i}", i == 1 ? null : i - 1, 0, TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined))
            .ToList();

        Should.Throw<InvalidOperationException>(() => JurisdictionChainResolver.Resolve(nodes, nodes.Count));
        JurisdictionChainResolver.Resolve(nodes, nodes.Count - 1).Count.ShouldBe(JurisdictionChainResolver.MaxJurisdictionDepth);
    }

    // Root TaxRoundingSettings case: the seed root sets both values.
    [Fact]
    public void Root_rounding_settings_resolve()
    {
        TaxRoundingSettings.Resolve([Wa]).ShouldBe(new TaxRoundingSettings(TaxRoundingMethod.HalfUp, TaxRoundingScope.Combined));
    }

    [Fact]
    public void Nearest_jurisdiction_wins_per_setting()
    {
        var seattle = new JurisdictionNode(2, "SEATTLE", "Seattle", ParentId: 1, 38_500, TaxRoundingMethod.Truncate, null);

        TaxRoundingSettings.Resolve([Wa, seattle]).ShouldBe(new TaxRoundingSettings(TaxRoundingMethod.Truncate, TaxRoundingScope.Combined));
    }

    [Fact]
    public void Root_without_both_settings_throws()
    {
        Should.Throw<InvalidOperationException>(() => TaxRoundingSettings.Resolve([Wa with { RoundingMethod = null }]))
            .Message.ShouldBe("Root tax jurisdiction WA must set tax_rounding_method and tax_rounding_scope.");
        Should.Throw<InvalidOperationException>(() => TaxRoundingSettings.Resolve([Wa with { RoundingScope = null }]));
    }
}
