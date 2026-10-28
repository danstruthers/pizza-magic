namespace PizzaMagic.Domain.Promotions;

/// <summary>Decides which gate-passing promotions are held when some cannot be combined.</summary>
public interface IConflictPolicy
{
    /// <summary>Returns one decision per candidate, in the same (entry) order.</summary>
    IReadOnlyList<ConflictDecision> Resolve(IReadOnlyList<ConflictCandidate> inEntryOrder, PromotionCatalog catalog);
}

public sealed record ConflictCandidate(string Code, PromotionDefinition Promotion);

/// <summary>Whether the candidate is held; if not, every held code it conflicts with, in entry order.</summary>
public sealed record ConflictDecision(bool Held, IReadOnlyList<string> ConflictsWith);
