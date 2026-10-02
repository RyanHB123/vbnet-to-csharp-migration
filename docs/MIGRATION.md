# Migration walkthrough

## 1. Establish two concrete implementations

The legacy solution runs on .NET Framework 4.8 with VB.NET, a Windows Forms desktop interface, classic project files, and a DataSet persisted as XML. The modern solution has a C# domain library, a C# JSON repository, a C# CLI, and a C# ASP.NET Core API serving a browser dashboard.

These are independently implemented applications with matching catalogue data and order workflows. The modern production code has no dependency on the legacy assembly. A separate test-only VB.NET project links the original source to support comparisons.

## 2. Translate the business rules into C#

The original calculator uses a DataTable, a customer-type string, and a positional decimal array. Its C# replacement uses validated InvoiceLine objects, a CustomerType enum, and named InvoiceTotals values. The compiler enables nullable reference analysis.

The calculation order remains explicit: subtotal, rounded trade discount, discounted net amount, rounded VAT, then gross total. Decimal arithmetic and midpoint rounding away from zero are preserved. A subtotal of 1,000.05 produces a 100.01 discount; that half-penny boundary has an independently specified expected value.

The original arbitrary-price calculator accepts wider inputs than the modern domain. The modern calculator deliberately rejects empty invoices, negative prices, nonpositive quantities, prices with excess decimal places, and excessive input sizes. The HTTP calculator route accepts Retail/Trade case-insensitively and rejects unknown customer types. These are documented validation changes; parity applies to accepted inputs with canonical customer types.

## 3. Expand a meaningful shared workflow

Both order stores support catalogue search, seeded customer profiles, quotes, placement, cancellation, history, sales reports, and CSV export. They enforce the same order rules:

1. Look up the customer and every SKU.
2. Reject duplicate SKUs, invalid quantities, and insufficient stock.
3. Calculate from catalogue prices; the caller cannot set order prices.
4. Snapshot each line's name, unit price, and quantity.
5. Deduct stock and add the order in one saved state change.
6. On cancellation, restore stock exactly once and retain the order in history.
7. Exclude cancelled orders from sales and units-sold metrics.

Quotes are read-only. Placement checks the current stock again, so a previously valid quote is not a reservation. Seeded customer profiles are pricing data, not authentication accounts.

## 4. Separate responsibilities in the modern version

| Layer | Responsibility |
|---|---|
| ModernInvoices.Core | Models, invoice calculation/policies, order processing, queries, storage contracts |
| ModernInvoices.Infrastructure | JSON persistence, transaction locking, CSV formatting, demo seed data |
| ModernInvoices.Api | HTTP input/output, dependency injection, static dashboard assets |
| ModernInvoices.Cli | Commands and console presentation |

The order service uses IOrderRepository callbacks. Each callback loads a fresh state; a mutation is committed only when the callback completes. The JSON repository serializes requests with a lock and replaces the file using a temporary snapshot. Reads and writes use the same lock. Failed validation leaves the saved file unchanged.

The modern design applies [SOLID principles](SOLID.md) explicitly. OrderService handles quote/place/cancel workflows. CatalogQueries, OrderQueries, and SalesReportQuery retrieve and summarize data through IOrderReader. CSV formatting lives in Infrastructure. InvoiceCalculator depends on discount/tax policy interfaces; OrderService depends on IInvoiceCalculator and an injected TimeProvider. The hosts select implementations at startup.

The legacy store keeps similar responsibilities together in one VB.NET class with DataTables and XML column names. This gives reviewers concrete before/after code to compare.

Both stores assume one application process per file. The modern API supports concurrent requests through its singleton repository, but multiple repository instances in separate processes have no shared lock. A real multi-instance deployment needs database transactions. The demo does not promise crash-proof storage, schema migrations, or automatic XML-to-JSON import.

## 5. Expose the workflow through HTTP and a dashboard

The API accepts SKU/quantity pairs, invokes the domain service, and returns explicit results: 201 for creation, 400 for invalid input, 404 for missing records, and 409 for stock/status conflicts. The original arbitrary-price quote endpoint remains as a simple regression example.

The browser UI provides a customer selector, editable basket, quote review, order placement, catalogue search, metrics, history filtering, details, cancellation, and CSV download. Browser-side checks improve feedback; the server remains responsible for validating prices, quantities, and stock.

Order snapshots allow historical invoices to remain accurate after a catalogue price changes. The current UI deliberately keeps catalogue/customer editing outside its scope.

## 6. Verify the translation and the workflow

The executable C# check runner has 21 groups:

- Independently specified totals, discount thresholds, midpoint rounding, and validation.
- 2,000 generated invoices compared with the original VB.NET calculator.
- Catalogue, quote, placement, stock, report, and cancellation parity.
- Reopening both stores, failed-order rollback, historic price snapshots, and corrupt-file handling.
- Concurrent requests for the last three stock units, accepting exactly three orders.
- The same repository contracts against JSON and in-memory implementations, including nested snapshot isolation and rollback.
- Alternative pricing policies and a fixed clock, queries without write access, rejected invalid policy output, and CSV escaping/culture.

The behavior comparisons compile the original VB.NET business-rule source on .NET 8. The separate Windows script builds `LegacyInvoices.sln` in Debug and Release and checks that the Windows Forms executable and symbols were produced. It does not automate the desktop interface or execute a headless .NET Framework comparison. Reviewers can run the two applications and compare the workflow directly.

The HTTP script starts a real Kestrel server with an isolated file and checks the public endpoints. GitHub Actions runs the modern checks on Windows and Linux and builds the Framework desktop app on Windows. Desktop UI checks are performed manually; they are not part of that automated workflow.

## 7. Explain the limits and next steps

This case study covers language/runtime translation, architecture, storage, and behavior preservation. It does not implement a live traffic migration, production rollout, authentication, payment processing, shipping, or a legacy-data importer. There are no invented performance or productivity claims.

Next steps can be incremental: upgrade to .NET 10, add a database with transactions, implement and verify an XML import command, add product/customer administration, and move the executable checks into a standard test framework with coverage reporting.

The project targets .NET 8 because it is available in the development environment. When upgrading, update global.json, all modern project targets, the script DLL paths, and the CI SDK version together.

References:

- [Microsoft: port from .NET Framework to .NET](https://learn.microsoft.com/en-us/dotnet/core/porting/framework-overview)
- [Microsoft: .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
