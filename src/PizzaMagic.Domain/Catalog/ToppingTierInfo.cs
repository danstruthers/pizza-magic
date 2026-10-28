namespace PizzaMagic.Domain.Catalog;

/// <summary>A topping tier as the menu shows it: code, name and display order.</summary>
public sealed record ToppingTierInfo(long Id, string Code, string DisplayName, int SortOrder);
