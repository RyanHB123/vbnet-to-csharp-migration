using ModernInvoices.Core;
using ModernInvoices.Infrastructure;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IDiscountPolicy, TradeDiscountPolicy>();
builder.Services.AddSingleton<ITaxPolicy, StandardVatPolicy>();
builder.Services.AddSingleton<IInvoiceCalculator, InvoiceCalculator>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IOrderRepository>(new JsonOrderRepository(
builder.Configuration["DataFile"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "orders.json")));
builder.Services.AddSingleton<IOrderReader>(services => services.GetRequiredService<IOrderRepository>());
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<CatalogQueries>();
builder.Services.AddSingleton<OrderQueries>();
builder.Services.AddSingleton<SalesReportQuery>();
builder.Services.AddSingleton<OrderCsvExporter>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api", () => Results.Ok(new
{
    name = "Legacy VB.NET → Modern .NET",
    description = "C# order management with a domain layer, JSON persistence, and a browser dashboard.",
    endpoint = "POST /api/invoices/quote",
    example = new { customerType = "Trade", lines = new[] { new { unitPrice = 125m, quantity = 8 }, new { unitPrice = 49.5m, quantity = 2 } } }
}));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
var orders = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    try { return await next(context); }
    catch (MissingRecordException exception) { return Results.Problem(exception.Message, statusCode: 404); }
    catch (OrderConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { [exception.ParamName ?? "order"] = [exception.Message] });
    }
});
orders.MapGet("/products", (string? search, CatalogQueries queries) => queries.GetProducts(search));
orders.MapGet("/customers", (CatalogQueries queries) => queries.GetCustomers());
orders.MapGet("/orders", (string? customerId, OrderQueries queries) => queries.GetOrders(customerId));
orders.MapGet("/orders/{id}", (string id, OrderQueries queries) => queries.GetOrder(id));
orders.MapPost("/orders/quote", (CreateOrderRequest request, OrderService service) =>
    service.Quote(request.CustomerId ?? "", request.ToLines()));
orders.MapPost("/orders", (CreateOrderRequest request, OrderService service) =>
{
    var order = service.Place(request.CustomerId ?? "", request.ToLines());
    return Results.Created($"/api/orders/{order.Id}", order);
});
orders.MapPost("/orders/{id}/cancel", (string id, OrderService service) => service.Cancel(id));
orders.MapGet("/reports/sales", (SalesReportQuery query) => query.GetReport());
orders.MapGet("/exports/orders.csv", (OrderQueries queries, OrderCsvExporter exporter) =>
    Results.File(Encoding.UTF8.GetBytes(exporter.Export(queries.GetOrders())), "text/csv; charset=utf-8", "orders.csv"));
app.MapPost("/api/invoices/quote", (QuoteRequest request, IInvoiceCalculator calculator) =>
{
    // Only documented names are accepted; numeric enum strings are rejected.
    CustomerType customer;
    if (string.Equals(request.CustomerType, "Trade", StringComparison.OrdinalIgnoreCase))
        customer = CustomerType.Trade;
    else if (string.Equals(request.CustomerType, "Retail", StringComparison.OrdinalIgnoreCase))
        customer = CustomerType.Retail;
    else
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["customerType"] = ["Use Retail or Trade."] });

    try
    {
        if (request.Lines is null || request.Lines.Any(line => line is null))
            throw new ArgumentException("Supply an array of invoice lines.", "lines");
        if (request.Lines.Any(line => line!.UnitPrice is null || line.Quantity is null))
            throw new ArgumentException("Every line requires unitPrice and quantity.", "lines");
        var lines = request.Lines.Select(line => new InvoiceLine(line!.UnitPrice!.Value, line.Quantity!.Value));
        return Results.Ok(calculator.Calculate(lines, customer));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "invoice"] = [exception.Message]
        });
    }
});
app.Run();

internal sealed record QuoteRequest(string? CustomerType, List<LineRequest?>? Lines);
internal sealed record LineRequest(decimal? UnitPrice, int? Quantity);
internal sealed record ProductLineRequest(string? Sku, int? Quantity);
internal sealed record CreateOrderRequest(string? CustomerId, List<ProductLineRequest?>? Lines)
{
    public OrderRequestLine[] ToLines()
    {
        if (Lines is null || Lines.Any(line => line is null || string.IsNullOrWhiteSpace(line.Sku) || line.Quantity is null))
            throw new ArgumentException("Each line requires sku and quantity.");
        return Lines.Select(line => new OrderRequestLine(line!.Sku!, line.Quantity!.Value)).ToArray();
    }
}
