namespace ModernInvoices.Core;

public enum CustomerType { Retail, Trade }

public sealed record InvoiceLine
{
    public InvoiceLine(decimal unitPrice, int quantity)
    {
        if (unitPrice is < 0m or > 1_000_000m)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price must be between 0 and 1,000,000.");
        if (decimal.Round(unitPrice, 2) != unitPrice)
            throw new ArgumentException("Unit price must have at most two decimal places.", nameof(unitPrice));
        if (quantity is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be between 1 and 10,000.");
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public decimal UnitPrice { get; }
    public int Quantity { get; }
}

public sealed record InvoiceTotals(decimal Subtotal, decimal Discount, decimal Vat)
{
    public decimal Total => Subtotal - Discount + Vat;
}
