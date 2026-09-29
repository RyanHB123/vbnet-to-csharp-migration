namespace ModernInvoices.Core;

public sealed record Product(string Sku, string Name, string Category, decimal UnitPrice, int Stock);
public sealed record Customer(string Id, string Name, CustomerType Type);
public sealed record OrderRequestLine(string Sku, int Quantity);
public sealed record OrderLine(string Sku, string Name, decimal UnitPrice, int Quantity);
public enum OrderStatus { Placed, Cancelled }
public sealed record Order(string Id, string CustomerId, string CustomerName, CustomerType CustomerType,
    DateTimeOffset CreatedAt, OrderStatus Status, OrderLine[] Lines, InvoiceTotals Totals);
public sealed record SalesReport(int ActiveOrders, int CancelledOrders, decimal NetSales,
    decimal Vat, decimal GrossSales, int UnitsSold, int LowStockProducts);

public sealed class MissingRecordException(string message) : Exception(message);
public sealed class OrderConflictException(string message) : Exception(message);
