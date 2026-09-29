namespace ModernInvoices.Core;

public sealed class CatalogQueries(IOrderReader repository)
{
    public Product[] GetProducts(string? search = null) => repository.Read(state => state.Products
        .Where(p => string.IsNullOrWhiteSpace(search) ||
            $"{p.Sku} {p.Name} {p.Category}".Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray());

    public Customer[] GetCustomers() => repository.Read(state => state.Customers.ToArray());
}
