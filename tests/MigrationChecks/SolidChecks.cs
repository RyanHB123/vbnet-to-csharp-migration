using System.Globalization;
using ModernInvoices.Core;
using ModernInvoices.Infrastructure;

internal static class SolidChecks
{
    public static void Run(Action<string, Action> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "solid-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            check("JSON repository satisfies snapshot, rollback, and concurrency contracts", () =>
                RepositoryContract(new JsonOrderRepository(Path.Combine(directory, "contract.json"))));
            check("in-memory repository satisfies the same contracts and order lifecycle", () =>
                RepositoryContract(new InMemoryOrderRepository(DemoStoreData.Create())));

            check("alternative pricing policies and clock work without changing order processing", () =>
            {
                var repository = new InMemoryOrderRepository(DemoStoreData.Create());
                IInvoiceCalculator pricing = new InvoiceCalculator(new NoDiscountPolicy(), new ZeroTaxPolicy());
                var clock = new FixedClock(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
                var service = new OrderService(repository, pricing, clock);
                OrderRequestLine[] lines = [new("MON-001", 8)];
                var quote = service.Quote("CUST-001", lines);
                var order = service.Place("CUST-001", lines);
                Require(quote.Discount == 0m && quote.Vat == 0m && quote.Total == 1000m, "Alternative pricing was ignored.");
                Require(order.Totals == quote, "Placement did not use the same pricing policy as the quote.");
                Require(order.CreatedAt == clock.GetUtcNow(), "Order creation did not use the supplied clock.");
            });

            check("query services work with a reader that has no write capability", () =>
            {
                IOrderReader reader = new SeedOnlyReader();
                Require(new CatalogQueries(reader).GetProducts("monitor").Length == 2, "Catalogue query failed.");
                Require(new OrderQueries(reader).GetOrders().Length == 0, "History query failed.");
                Require(new SalesReportQuery(reader).GetReport().LowStockProducts == 2, "Report query failed.");
            });

            check("invalid policy output cannot commit an order or deduct stock", () =>
            {
                foreach (var calculator in new IInvoiceCalculator[]
                {
                    new InvoiceCalculator(new ExcessiveDiscountPolicy(), new StandardVatPolicy()),
                    new InvoiceCalculator(new TradeDiscountPolicy(), new NegativeTaxPolicy())
                })
                {
                    var repository = new InMemoryOrderRepository(DemoStoreData.Create());
                    var service = new OrderService(repository, calculator, TimeProvider.System);
                    var rejected = false;
                    try { service.Place("CUST-001", [new("MON-001", 1)]); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "Invalid policy output was accepted.");
                    Require(new OrderQueries(repository).GetOrders().Length == 0, "Invalid order was saved.");
                    Require(new CatalogQueries(repository).GetProducts("MON-001").Single().Stock == 24, "Invalid order changed stock.");
                }
            });

            check("CSV formatting escapes customer names and is independent of current culture", () =>
            {
                var culture = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    var order = new Order("ORD-example", "CUST-001", "Studio, \"North\"\nDesign", CustomerType.Trade,
                        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), OrderStatus.Placed,
                        [new("MON-001", "Monitor", 125m, 1)], new InvoiceTotals(125m, 0m, 25m));
                    var csv = new OrderCsvExporter().Export([order]);
                    Require(csv.Contains("\"Studio, \"\"North\"\"\nDesign\""), "CSV customer escaping is incorrect.");
                    Require(csv.EndsWith(",150.00\r\n", StringComparison.Ordinal), "CSV amount depends on machine culture.");
                }
                finally { CultureInfo.CurrentCulture = culture; }
            });
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    // The exact same observable behavior is required from both storage implementations.
    private static void RepositoryContract(IOrderRepository repository)
    {
        var originalCount = repository.Read(state => state.Products.Count);
        repository.Read(state => { state.Products.Clear(); return true; });
        Require(repository.Read(state => state.Products.Count) == originalCount, "A read mutated committed state.");

        var retained = repository.Update(state =>
        {
            state.Products[0] = state.Products[0] with { Stock = 10 };
            return state;
        });
        retained.Products.Clear();
        Require(repository.Read(state => state.Products[0].Stock) == 10, "A returned reference changed committed state.");

        var failure = new InvalidOperationException("Simulated business failure");
        Exception? observed = null;
        try { repository.Update<bool>(state => { state.Products.Clear(); throw failure; }); }
        catch (InvalidOperationException exception) { observed = exception; }
        Require(ReferenceEquals(failure, observed), "The callback exception was not propagated.");
        Require(repository.Read(state => state.Products[0].Stock) == 10, "A failed transaction committed partial changes.");

        var service = new OrderService(repository,
            new InvoiceCalculator(new TradeDiscountPolicy(), new StandardVatPolicy()), TimeProvider.System);
        var queries = new OrderQueries(repository);
        var placed = service.Place("CUST-002", [new("MON-001", 1)]);
        placed.Lines[0] = placed.Lines[0] with { Quantity = 999 };
        var read = queries.GetOrder(placed.Id);
        Require(read.Lines[0].Quantity == 1, "Mutating the command result changed the saved order.");
        read.Lines[0] = read.Lines[0] with { Quantity = 999 };
        Require(queries.GetOrder(placed.Id).Lines[0].Quantity == 1, "Mutating a query result changed the saved order.");
        service.Cancel(placed.Id);
        Require(repository.Read(state => state.Products[0].Stock) == 10, "Cancellation did not restore the original quantity.");
        Require(queries.GetOrder(placed.Id).Status == OrderStatus.Cancelled, "Order lifecycle differs between stores.");

        var accepted = 0;
        Parallel.For(0, 12, _ =>
        {
            try { service.Place("CUST-002", [new("HUB-006", 1)]); Interlocked.Increment(ref accepted); }
            catch (OrderConflictException) { }
        });
        Require(accepted == 3, "Concurrent transactions oversold or lost stock.");
        Require(new SalesReportQuery(repository).GetReport().UnitsSold == 3, "Committed orders were lost.");
        Require(repository.Read(state => state.Products.Single(p => p.Sku == "HUB-006").Stock) == 0, "Stock is inconsistent.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class NoDiscountPolicy : IDiscountPolicy
    {
        public decimal CalculateDiscount(decimal subtotal, CustomerType customer) => 0m;
    }
    private sealed class ZeroTaxPolicy : ITaxPolicy
    {
        public decimal CalculateTax(decimal taxableAmount) => 0m;
    }
    private sealed class ExcessiveDiscountPolicy : IDiscountPolicy
    {
        public decimal CalculateDiscount(decimal subtotal, CustomerType customer) => subtotal + 1m;
    }
    private sealed class NegativeTaxPolicy : ITaxPolicy
    {
        public decimal CalculateTax(decimal taxableAmount) => -1m;
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class SeedOnlyReader : IOrderReader
    {
        public T Read<T>(Func<StoreState, T> query) => query(DemoStoreData.Create());
    }
}
