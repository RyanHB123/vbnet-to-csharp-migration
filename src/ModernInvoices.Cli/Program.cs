using System.Globalization;
using ModernInvoices.Core;
using ModernInvoices.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;

var calculator = new InvoiceCalculator(new TradeDiscountPolicy(), new StandardVatPolicy());
var store = new JsonOrderRepository(Environment.GetEnvironmentVariable("MODERN_DATA_FILE")
    ?? Path.Combine(AppContext.BaseDirectory, "data", "orders.json"));
var service = new OrderService(store, calculator, TimeProvider.System);
var catalog = new CatalogQueries(store);
var history = new OrderQueries(store);
var reports = new SalesReportQuery(store);
var exporter = new OrderCsvExporter();
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
try
{
    if (args is ["--interactive"])
    {
        Console.WriteLine("MODERN ORDER DESK | C# / .NET 8");
        Help();
        while (true)
        {
            Console.Write("\nmodern> ");
            var input = Console.ReadLine();
            if (input is null || input.Trim() == "exit") break;
            try { Execute(input.Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
            catch (Exception exception) { Console.WriteLine($"Error: {exception.Message}"); }
        }
    }
    else Execute(args);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}

void Help() => Console.WriteLine("""
products [search] | customers | orders [customer-id] | report
quote CUST-001 MON-001:8 DOCK-002:2
place CUST-001 MON-001:8 DOCK-002:2
cancel order-id | export | invoice | help | exit
Set MODERN_DATA_FILE to choose a separate JSON data file.
""");

void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, jsonOptions));

void PrintTotals(InvoiceTotals totals)
{
    Console.WriteLine($"Subtotal: {totals.Subtotal.ToString("F2", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Discount: {totals.Discount.ToString("F2", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"VAT: {totals.Vat.ToString("F2", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Total: {totals.Total.ToString("F2", CultureInfo.InvariantCulture)}");
}

void Execute(string[] arguments)
{
    var command = arguments.Length == 0 ? "invoice" : arguments[0].ToLowerInvariant();
    switch (command)
    {
        case "invoice": PrintTotals(calculator.Calculate([new(125m, 8), new(49.5m, 2)], CustomerType.Trade)); break;
        case "products": Print(catalog.GetProducts(string.Join(' ', arguments.Skip(1)))); break;
        case "customers": Print(catalog.GetCustomers()); break;
        case "orders": Print(history.GetOrders(arguments.Length > 1 ? arguments[1] : null)); break;
        case "quote":
        case "place":
            if (arguments.Length < 3) throw new ArgumentException("Supply customer-id and SKU:quantity pairs.");
            var lines = arguments.Skip(2).Select(item =>
            {
                var pair = item.Split(':');
                if (pair.Length != 2) throw new ArgumentException("Use SKU:quantity.");
                return new OrderRequestLine(pair[0], int.Parse(pair[1], CultureInfo.InvariantCulture));
            }).ToArray();
            if (command == "quote") PrintTotals(service.Quote(arguments[1], lines));
            else Print(service.Place(arguments[1], lines));
            break;
        case "cancel":
            if (arguments.Length != 2) throw new ArgumentException("Supply an order-id.");
            Print(service.Cancel(arguments[1])); break;
        case "report": Print(reports.GetReport()); break;
        case "export": Console.Write(exporter.Export(history.GetOrders())); break;
        case "help": Help(); break;
        default: throw new ArgumentException("Unknown command. Use help.");
    }
}
