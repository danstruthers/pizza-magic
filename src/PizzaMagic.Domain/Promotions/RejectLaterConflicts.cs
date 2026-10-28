namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// User decision UQ3: the earlier-entered code always holds, and a later code that conflicts with any held
/// code is rejected. Conflicts are static (they depend only on the definitions, not on eligibility), so a
/// held code that is not yet eligible still blocks.
/// </summary>
public sealed class RejectLaterConflicts : IConflictPolicy
{
    public IReadOnlyList<ConflictDecision> Resolve(IReadOnlyList<ConflictCandidate> inEntryOrder, PromotionCatalog catalog)
    {
        var pairs = catalog.Incompatibilities
            .Select(p => (Math.Min(p.PromotionIdLow, p.PromotionIdHigh), Math.Max(p.PromotionIdLow, p.PromotionIdHigh)))
            .ToHashSet();
        var held = new List<ConflictCandidate>();
        var decisions = new List<ConflictDecision>(inEntryOrder.Count);

        foreach (var candidate in inEntryOrder)
        {
            var blockers = held.Where(h => Conflicts(h.Promotion, candidate.Promotion, pairs)).Select(h => h.Code).ToList();
            if (blockers.Count == 0)
            {
                held.Add(candidate);
            }
            decisions.Add(new ConflictDecision(blockers.Count == 0, blockers));
        }
        return decisions;
    }

    private static bool Conflicts(PromotionDefinition a, PromotionDefinition b, HashSet<(long, long)> pairs) =>
        a.IsExclusive
        || b.IsExclusive
        || (a.ExclusionGroup is not null && a.ExclusionGroup == b.ExclusionGroup)
        || pairs.Contains((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)));
}
