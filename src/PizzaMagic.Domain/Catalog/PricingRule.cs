namespace PizzaMagic.Domain.Catalog;

/// <summary>
/// A topping pricing rule as stored in the database: a method code plus its parameters,
/// e.g. ("percent_of_base", 80 000 ppm, null) or ("fixed_amount", null, 100 cents).
/// </summary>
public sealed record PricingRule(string Method, int? RatePpm, long? AmountCents);
