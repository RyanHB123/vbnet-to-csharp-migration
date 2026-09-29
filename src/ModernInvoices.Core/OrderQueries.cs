namespace ModernInvoices.Core;

public sealed class OrderQueries(IOrderReader repository)
{
    public Order[] GetOrders(string? customerId = null) => repository.Read(state => state.Orders
        .Where(o => customerId is null || o.CustomerId == customerId)
        .OrderByDescending(o => o.CreatedAt).ToArray());

    public Order GetOrder(string id) => repository.Read(state => state.Orders.FirstOrDefault(o => o.Id == id)
        ?? throw new MissingRecordException("Order was not found."));
}
