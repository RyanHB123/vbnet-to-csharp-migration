namespace ModernInvoices.Core;

public sealed class SalesReportQuery(IOrderReader repository)
{
    public SalesReport GetReport() => repository.Read(state =>
    {
        var active = state.Orders.Where(o => o.Status == OrderStatus.Placed).ToArray();
        return new SalesReport(active.Length, state.Orders.Count - active.Length,
            active.Sum(o => o.Totals.Subtotal - o.Totals.Discount), active.Sum(o => o.Totals.Vat),
            active.Sum(o => o.Totals.Total), active.Sum(o => o.Lines.Sum(line => line.Quantity)),
            state.Products.Count(p => p.Stock <= 5));
    });
}
