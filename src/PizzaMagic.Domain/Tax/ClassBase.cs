using PizzaMagic.Domain.Monetary;

namespace PizzaMagic.Domain.Tax;

/// <summary>
/// One tax class's share of an order: its gross amount and the discount allocated to it (plan section 2.8).
/// The taxable base is gross minus discount.
/// </summary>
public sealed record ClassBase
{
    public ClassBase(long TaxClassId, int SortOrder, Money Gross, Money Discount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Gross.Cents, nameof(Gross));
        ArgumentOutOfRangeException.ThrowIfNegative(Discount.Cents, nameof(Discount));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Discount.Cents, Gross.Cents, nameof(Discount));
        this.TaxClassId = TaxClassId;
        this.SortOrder = SortOrder;
        this.Gross = Gross;
        this.Discount = Discount;
    }

    public long TaxClassId { get; }

    public int SortOrder { get; }

    public Money Gross { get; }

    public Money Discount { get; }

    public Money Taxable => Gross - Discount;
}
