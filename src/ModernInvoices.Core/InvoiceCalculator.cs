namespace ModernInvoices.Core;

public sealed class InvoiceCalculator(IDiscountPolicy discounts, ITaxPolicy taxes) : IInvoiceCalculator
{
    public InvoiceTotals Calculate(IEnumerable<InvoiceLine> lines, CustomerType customer)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!Enum.IsDefined(customer)) throw new ArgumentOutOfRangeException(nameof(customer));
        var items = lines.Take(101).ToArray();
        if (items.Length is < 1 or > 100)
            throw new ArgumentException("An invoice must contain between 1 and 100 lines.", nameof(lines));
        if (items.Any(item => item is null))
            throw new ArgumentException("Invoice lines cannot be null.", nameof(lines));
        var subtotal = items.Sum(item => item.UnitPrice * item.Quantity);
        var rawDiscount = discounts.CalculateDiscount(subtotal, customer);
        if (rawDiscount < 0m || rawDiscount > subtotal)
            throw new InvalidOperationException("Discount policy returned an amount outside the subtotal.");
        var discount = decimal.Round(rawDiscount, 2, MidpointRounding.AwayFromZero);
        var rawTax = taxes.CalculateTax(subtotal - discount);
        if (rawTax < 0m) throw new InvalidOperationException("Tax policy returned a negative amount.");
        var vat = decimal.Round(rawTax, 2, MidpointRounding.AwayFromZero);
        return new(subtotal, discount, vat);
    }
}
