using System.Globalization;
using ModernInvoices.Core;

namespace ModernInvoices.Infrastructure;

// Output formatting has no dependency on storage, HTTP, or order-processing services.
public sealed class OrderCsvExporter
{
    public string Export(IEnumerable<Order> orders)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var rows = orders.Select(o => string.Join(",", Cell(o.Id), Cell(o.CustomerName),
            o.Status.ToString(), o.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            o.Totals.Total.ToString("F2", CultureInfo.InvariantCulture)));
        return "OrderId,Customer,Status,CreatedAt,Total\r\n" + string.Join("\r\n", rows) + "\r\n";
    }
}
