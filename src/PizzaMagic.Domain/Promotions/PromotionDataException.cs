namespace PizzaMagic.Domain.Promotions;

/// <summary>Promotion data that cannot be used: the promotion fails closed as Misconfigured.</summary>
public sealed class PromotionDataException(string message) : Exception(message);
