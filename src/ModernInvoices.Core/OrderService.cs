namespace ModernInvoices.Core;

public sealed class OrderService(IOrderRepository repository, IInvoiceCalculator calculator, TimeProvider clock)
{
    private (Customer Customer, OrderLine[] Lines, InvoiceTotals Totals) Prepare(
        StoreState state, string customerId, IReadOnlyList<OrderRequestLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count is < 1 or > 100) throw new ArgumentException("Choose between 1 and 100 products.");
        if (lines.Any(line => line is null || string.IsNullOrWhiteSpace(line.Sku)))
            throw new ArgumentException("Each line requires a product SKU.");
        if (lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).Count() != lines.Count)
            throw new ArgumentException("Combine duplicate products into a single line.");
        var customer = state.Customers.FirstOrDefault(c => c.Id == customerId)
            ?? throw new MissingRecordException("Customer was not found.");
        var snapshots = lines.Select(line =>
        {
            var product = state.Products.FirstOrDefault(p => p.Sku == line.Sku)
                ?? throw new MissingRecordException($"Product {line.Sku} was not found.");
            _ = new InvoiceLine(product.UnitPrice, line.Quantity);
            if (line.Quantity > product.Stock) throw new OrderConflictException($"Only {product.Stock} units of {product.Sku} remain.");
            return new OrderLine(product.Sku, product.Name, product.UnitPrice, line.Quantity);
        }).ToArray();
        var totals = calculator.Calculate(snapshots.Select(line => new InvoiceLine(line.UnitPrice, line.Quantity)), customer.Type);
        return (customer, snapshots, totals);
    }

    public InvoiceTotals Quote(string customerId, IReadOnlyList<OrderRequestLine> lines) =>
        repository.Read(state => Prepare(state, customerId, lines).Totals);

    public Order Place(string customerId, IReadOnlyList<OrderRequestLine> lines) => repository.Update(state =>
    {
        var prepared = Prepare(state, customerId, lines);
        var order = new Order($"ORD-{Guid.NewGuid():N}", prepared.Customer.Id, prepared.Customer.Name,
            prepared.Customer.Type, clock.GetUtcNow(), OrderStatus.Placed, prepared.Lines, prepared.Totals);
        foreach (var line in order.Lines)
        {
            var index = state.Products.FindIndex(p => p.Sku == line.Sku);
            state.Products[index] = state.Products[index] with { Stock = state.Products[index].Stock - line.Quantity };
        }
        state.Orders.Add(order);
        return order;
    });

    public Order Cancel(string id) => repository.Update(state =>
    {
        var index = state.Orders.FindIndex(o => o.Id == id);
        if (index < 0) throw new MissingRecordException("Order was not found.");
        var order = state.Orders[index];
        if (order.Status == OrderStatus.Cancelled) throw new OrderConflictException("Order is already cancelled.");
        foreach (var line in order.Lines)
        {
            var productIndex = state.Products.FindIndex(p => p.Sku == line.Sku);
            state.Products[productIndex] = state.Products[productIndex] with { Stock = state.Products[productIndex].Stock + line.Quantity };
        }
        return state.Orders[index] = order with { Status = OrderStatus.Cancelled };
    });

}
