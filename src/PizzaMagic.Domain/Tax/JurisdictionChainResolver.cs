namespace PizzaMagic.Domain.Tax;

/// <summary>Resolves a restaurant's jurisdiction plus all its ancestors, outermost first (depth 0 = root).</summary>
public static class JurisdictionChainResolver
{
    public const int MaxJurisdictionDepth = 16;

    public static IReadOnlyList<JurisdictionNode> Resolve(IReadOnlyList<JurisdictionNode> jurisdictions, long jurisdictionId)
    {
        var byId = jurisdictions.ToDictionary(j => j.Id);
        var visited = new HashSet<long>();
        var chain = new List<JurisdictionNode>();

        for (long? id = jurisdictionId; id is { } current; id = byId[current].ParentId)
        {
            if (!byId.TryGetValue(current, out var node))
            {
                throw new InvalidOperationException($"Tax jurisdiction {current} does not exist.");
            }
            if (!visited.Add(current))
            {
                throw new InvalidOperationException($"Tax jurisdiction {node.Code} is part of a cycle.");
            }
            if (chain.Count == MaxJurisdictionDepth)
            {
                throw new InvalidOperationException($"Tax jurisdiction chain from {jurisdictionId} is deeper than {MaxJurisdictionDepth} levels.");
            }
            chain.Add(node);
        }

        chain.Reverse();
        return chain;
    }
}
