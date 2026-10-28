namespace PizzaMagic.Domain.Catalog;

/// <summary>The result of looking a code up on a restaurant's effective menu.</summary>
public enum ProductLookup
{
    Available,
    Unavailable,
    WrongType,
    Unknown,
}
