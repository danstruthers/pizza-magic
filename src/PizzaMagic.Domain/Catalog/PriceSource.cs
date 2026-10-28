namespace PizzaMagic.Domain.Catalog;

/// <summary>Where an effective price came from; snapshotted on orders.</summary>
public enum PriceSource
{
    Global,
    Restaurant,
}
