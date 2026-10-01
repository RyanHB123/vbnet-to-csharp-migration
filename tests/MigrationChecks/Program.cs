using System.Data;
using ModernInvoices.Core;
using ModernInvoices.Infrastructure;

var checks = 0;
var calculator = new InvoiceCalculator(new TradeDiscountPolicy(), new StandardVatPolicy());

void Equal(decimal expected, decimal actual, string label)
{
    if (expected != actual) throw new Exception($"{label}: expected {expected}, got {actual}");
}

void Check(string name, Action action)
{
    action();
    checks++;
    Console.WriteLine($"PASS {name}");
}

void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected validation to reject the input.");
}

void Parity(InvoiceLine[] lines, CustomerType customer)
{
    var table = new DataTable();
    table.Columns.Add("UnitPrice", typeof(decimal));
    table.Columns.Add("Quantity", typeof(int));
    foreach (var line in lines) table.Rows.Add(line.UnitPrice, line.Quantity);
    var legacy = LegacyInvoices.InvoiceCalculator.Calculate(table, customer.ToString());
    var modern = calculator.Calculate(lines, customer);
    Equal(legacy[0], modern.Subtotal, "subtotal");
    Equal(legacy[1], modern.Discount, "discount");
    Equal(legacy[2], modern.Vat, "VAT");
    Equal(legacy[3], modern.Total, "total");
}

Check("documented trade invoice", () =>
{
    var total = calculator.Calculate([new(125m, 8), new(49.5m, 2)], CustomerType.Trade);
    Equal(1099m, total.Subtotal, "subtotal");
    Equal(109.90m, total.Discount, "discount");
    Equal(197.82m, total.Vat, "VAT");
    Equal(1186.92m, total.Total, "total");
});
Check("discount boundary and midpoint rounding", () =>
{
    Equal(0m, calculator.Calculate([new(999.99m, 1)], CustomerType.Trade).Discount, "below threshold");
    Equal(100m, calculator.Calculate([new(1000m, 1)], CustomerType.Trade).Discount, "at threshold");
    Equal(100.01m, calculator.Calculate([new(1000.05m, 1)], CustomerType.Trade).Discount, "half penny rounds away");
    Equal(0m, calculator.Calculate([new(1000m, 1)], CustomerType.Retail).Discount, "retail");
});
Check("parity across boundaries and zero price", () =>
{
    foreach (var price in new[] { 0m, 0.01m, 999.99m, 1000m, 1000.05m, 1000.06m, 1000000m })
        foreach (var customer in Enum.GetValues<CustomerType>())
            Parity([new(price, 1)], customer);
});
Check("2,000 reproducible generated invoice comparisons", () =>
{
    var random = new Random(42);
    for (var index = 0; index < 2000; index++)
    {
        var lines = Enumerable.Range(0, random.Next(1, 20))
            .Select(_ => new InvoiceLine(random.Next(0, 200001) / 100m, random.Next(1, 30))).ToArray();
        Parity(lines, index % 2 == 0 ? CustomerType.Trade : CustomerType.Retail);
    }
});
Check("invalid prices and quantities are rejected", () =>
{
    Reject(() => new InvoiceLine(-1m, 1));
    Reject(() => new InvoiceLine(1.001m, 1));
    Reject(() => new InvoiceLine(1000000.01m, 1));
    Reject(() => new InvoiceLine(1m, 0));
    Reject(() => new InvoiceLine(1m, 10001));
});
Check("invalid invoices are rejected", () =>
{
    Reject(() => calculator.Calculate(null!, CustomerType.Retail));
    Reject(() => calculator.Calculate([], CustomerType.Retail));
    Reject(() => calculator.Calculate([null!], CustomerType.Retail));
    Reject(() => calculator.Calculate([new(1m, 1)], (CustomerType)99));
    Reject(() => calculator.Calculate(Enumerable.Repeat(new InvoiceLine(1m, 1), 101), CustomerType.Retail));
});
Check("maximum accepted invoice remains within decimal range", () =>
{
    Equal(1080000000000m, calculator.Calculate(
        Enumerable.Repeat(new InvoiceLine(1000000m, 10000), 100), CustomerType.Trade).Total, "total");
});
var directory = Path.Combine(Path.GetTempPath(), "migration-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var modernPath = Path.Combine(directory, "modern.json");
    var legacyPath = Path.Combine(directory, "legacy.xml");
    var repository = new JsonOrderRepository(modernPath);
    var service = new OrderService(repository, calculator, TimeProvider.System);
    var catalog = new CatalogQueries(repository);
    var history = new OrderQueries(repository);
    var reports = new SalesReportQuery(repository);
    var exporter = new OrderCsvExporter();
    var legacyStore = new LegacyInvoices.OrderStore(legacyPath);
    var request = new[] { new OrderRequestLine("MON-001", 8), new OrderRequestLine("DOCK-002", 2) };
    var skus = new[] { "MON-001", "DOCK-002" };
    var quantities = new[] { 8, 2 };
    string modernId = "", legacyId = "";

    void StockParity()
    {
        var oldProducts = legacyStore.GetProducts();
        foreach (var product in catalog.GetProducts())
        {
            var old = oldProducts.Rows.Find(product.Sku)!;
            Equal((decimal)old["UnitPrice"], product.UnitPrice, "catalogue price");
            Equal((int)old["Stock"], product.Stock, "stock");
        }
    }

    void ReportParity()
    {
        var old = legacyStore.GetReport();
        var current = reports.GetReport();
        decimal[] values = [current.ActiveOrders, current.CancelledOrders, current.NetSales, current.Vat,
            current.GrossSales, current.UnitsSold, current.LowStockProducts];
        for (var i = 0; i < values.Length; i++) Equal(old[i], values[i], "report field " + i);
    }

    Check("catalogues, customers, search, and quotes match without reserving stock", () =>
    {
        StockParity();
        Equal(6, catalog.GetProducts().Length, "catalogue size");
        Equal(3, catalog.GetCustomers().Length, "customer count");
        Equal(legacyStore.GetProducts("monitor").Rows.Count, catalog.GetProducts("monitor").Length, "search");
        Equal(legacyStore.Quote("CUST-001", skus, quantities)[3], service.Quote("CUST-001", request).Total, "quote");
        Equal(24, catalog.GetProducts().Single(p => p.Sku == "MON-001").Stock, "quote leaves stock alone");
        if (File.Exists(modernPath) || File.Exists(legacyPath)) throw new Exception("Quote unexpectedly persisted state.");
    });
    Check("placing an order persists totals and deducts the same stock", () =>
    {
        legacyId = legacyStore.Place("CUST-001", skus, quantities);
        var order = service.Place("CUST-001", request);
        modernId = order.Id;
        Equal(1186.92m, order.Totals.Total, "placed total");
        Equal(16, catalog.GetProducts().Single(p => p.Sku == "MON-001").Stock, "stock after placing");
        StockParity(); ReportParity();
        Equal(1, history.GetOrders("CUST-001").Length, "customer history");
        Equal(0, history.GetOrders("CUST-002").Length, "history filter");
    });
    Check("both stores survive reopening", () =>
    {
        repository = new JsonOrderRepository(modernPath);
        service = new OrderService(repository, calculator, TimeProvider.System);
        catalog = new CatalogQueries(repository);
        history = new OrderQueries(repository);
        reports = new SalesReportQuery(repository);
        legacyStore = new LegacyInvoices.OrderStore(legacyPath);
        Equal(1186.92m, history.GetOrder(modernId).Totals.Total, "reopened total");
        Equal(1, legacyStore.GetOrders().Rows.Count, "legacy persisted order");
        StockParity(); ReportParity();
    });
    Check("failed multi-line orders leave the persisted files unchanged", () =>
    {
        var beforeModern = File.ReadAllText(modernPath);
        var beforeLegacy = File.ReadAllText(legacyPath);
        try { service.Place("CUST-001", [new("MON-001", 1), new("HUB-006", 4)]); throw new Exception("Expected stock conflict."); }
        catch (OrderConflictException) { }
        try { legacyStore.Place("CUST-001", ["MON-001", "HUB-006"], [1, 4]); throw new Exception("Expected stock conflict."); }
        catch (InvalidOperationException) { }
        Reject(() => service.Place("CUST-001", [new("MON-001", 1), new("MON-001", 1)]));
        Reject(() => legacyStore.Place("CUST-001", ["MON-001", "MON-001"], [1, 1]));
        Reject(() => service.Place("CUST-001", [new("MON-001", 0)]));
        Reject(() => legacyStore.Place("CUST-001", ["MON-001"], [0]));
        if (File.ReadAllText(modernPath) != beforeModern || File.ReadAllText(legacyPath) != beforeLegacy)
            throw new Exception("Rejected order modified persisted state.");
    });
    Check("cancellation restores stock once and removes sales from the report", () =>
    {
        service.Cancel(modernId); legacyStore.Cancel(legacyId);
        Equal(24, catalog.GetProducts().Single(p => p.Sku == "MON-001").Stock, "restocked");
        Equal(0, reports.GetReport().GrossSales, "cancelled sales excluded");
        Equal(1, reports.GetReport().CancelledOrders, "cancelled history retained");
        try { service.Cancel(modernId); throw new Exception("Expected repeated cancellation conflict."); }
        catch (OrderConflictException) { }
        try { legacyStore.Cancel(legacyId); throw new Exception("Expected repeated cancellation conflict."); }
        catch (InvalidOperationException) { }
        StockParity(); ReportParity();
        if (!exporter.Export(history.GetOrders()).Contains("Cancelled") || !legacyStore.ExportCsv().Contains("1186.92"))
            throw new Exception("CSV export lost the cancelled order.");
    });
    Check("price snapshots survive later catalogue changes", () =>
    {
        repository.Update(state => { state.Products[0] = state.Products[0] with { UnitPrice = 200m }; return true; });
        Equal(125m, history.GetOrder(modernId).Lines[0].UnitPrice, "historic unit price");
        Equal(1186.92m, history.GetOrder(modernId).Totals.Total, "historic total");
    });
    Check("concurrent requests cannot oversell the last three units", () =>
    {
        var concurrentRepository = new JsonOrderRepository(Path.Combine(directory, "concurrent.json"));
        var concurrentService = new OrderService(concurrentRepository, calculator, TimeProvider.System);
        var successful = 0;
        Parallel.For(0, 12, _ =>
        {
            try { concurrentService.Place("CUST-002", [new("HUB-006", 1)]); Interlocked.Increment(ref successful); }
            catch (OrderConflictException) { }
        });
        Equal(3, successful, "accepted orders");
        Equal(0, new CatalogQueries(concurrentRepository).GetProducts().Single(p => p.Sku == "HUB-006").Stock, "remaining stock");
        Equal(3, new SalesReportQuery(concurrentRepository).GetReport().UnitsSold, "sold units");
    });
    Check("invalid storage is surfaced and never silently replaced", () =>
    {
        var path = Path.Combine(directory, "invalid.json");
        File.WriteAllText(path, "{broken");
        try { new JsonOrderRepository(path).Read(state => state.Orders.Count); throw new Exception("Expected invalid JSON error."); }
        catch (System.Text.Json.JsonException) { }
        if (File.ReadAllText(path) != "{broken") throw new Exception("Invalid file was overwritten.");
    });
}
finally
{
    foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
    Directory.Delete(directory);
}
SolidChecks.Run(Check);
Console.WriteLine($"All {checks} check groups passed.");
