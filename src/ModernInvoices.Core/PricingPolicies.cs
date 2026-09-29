namespace ModernInvoices.Core;

public interface IInvoiceCalculator
{
    InvoiceTotals Calculate(IEnumerable<InvoiceLine> lines, CustomerType customer);
}

public interface IDiscountPolicy
{
    /// <summary>For a nonnegative subtotal and valid customer, return an unrounded amount from zero to subtotal.</summary>
    decimal CalculateDiscount(decimal subtotal, CustomerType customer);
}

public interface ITaxPolicy
{
    /// <summary>For a nonnegative discounted amount, return a nonnegative unrounded tax amount.</summary>
    decimal CalculateTax(decimal taxableAmount);
}

// These policies reproduce the legacy rules. The calculator owns monetary rounding.
public sealed class TradeDiscountPolicy : IDiscountPolicy
{
    public decimal CalculateDiscount(decimal subtotal, CustomerType customer) =>
        customer == CustomerType.Trade && subtotal >= 1000m ? subtotal * 0.1m : 0m;
}

public sealed class StandardVatPolicy : ITaxPolicy
{
    public decimal CalculateTax(decimal taxableAmount) => taxableAmount * 0.2m;
}
