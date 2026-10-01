using ModernInvoices.Core;

// A storage substitute for contract tests, with no file or JSON dependency.
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private StoreState state;
    private readonly object gate = new();

    public InMemoryOrderRepository(StoreState initial) => state = Copy(initial);

    public T Read<T>(Func<StoreState, T> query)
    {
        lock (gate) return query(Copy(state));
    }

    public T Update<T>(Func<StoreState, T> transaction)
    {
        lock (gate)
        {
            var draft = Copy(state);
            var result = transaction(draft);
            state = Copy(draft);
            return result;
        }
    }

    private static StoreState Copy(StoreState source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Products = [.. source.Products],
        Customers = [.. source.Customers],
        Orders = source.Orders.Select(order => order with { Lines = [.. order.Lines] }).ToList()
    };
}
