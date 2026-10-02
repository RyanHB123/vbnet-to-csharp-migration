# Five-minute presentation

## 0:00 — Introduce the migration

“This is a synthetic order-management migration case study. The original application is VB.NET on .NET Framework 4.8. The replacement is C# on .NET 8. Both support the same order workflow, and the modern version adds a browser dashboard.”

Open LegacyInvoices.sln and MigrationDemo.sln side by side. Be clear that this is a portfolio demo, not a commercial project you delivered.

## 0:45 — Show the legacy workflow

Start `LegacyInvoices.Desktop` from `LegacyInvoices.sln`. Show the catalogue and customers, add 8 monitors and 2 docks for Northwind Studio, then review the quote. Place the order and show the confirmation, stock deduction, history, and sales report. Cancel it to show stock restoration.

Explain the DataSet/XML storage and the original calculator's DataTable inputs and positional outputs. Open `legacy/LegacyInvoices.Business/OrderStore.vb` and `InvoiceCalculator.vb`.

## 1:30 — Show the C# architecture

Open src/ModernInvoices.Core/InvoiceCalculator.cs and OrderService.cs, then the JSON repository in Infrastructure. Explain validated models, named totals, the repository boundary, saved price snapshots, and stock checks before a transaction is committed.

Show docs/SOLID.md: order processing, queries, reporting, and CSV formatting each have a focused class. Pricing policies can be replaced; read-only consumers depend only on IOrderReader. The same repository contract checks run against JSON and an in-memory substitute.

The VB.NET LegacyBaseline project in the modern solution is test-only. All modern application code is C#; the browser uses standard HTML/CSS/JavaScript.

## 2:15 — Demonstrate the dashboard

Start ModernInvoices.Api using the README command and open http://localhost:5080. Select Northwind Studio and keep the initial 8 monitors plus 2 docks. Review the £1,186.92 total, then place the order.

Show the updated metrics, reduced stock, and new history row. Click the order ID to view its saved prices. Search for a product and download the CSV. Cancel the order and show stock restored with the cancelled history preserved.

Data survives restarts. For a fresh repeatable demo, start with a new data-file path, for example:

```powershell
dotnet run --project src/ModernInvoices.Api -c Release --no-build -- --DataFile "C:\path\to\fresh-demo.json"
```

Use your own absolute path and a new filename. Each store is intended for one application process at a time.

## 3:30 — Run the evidence

After restoring/building:

```sh
dotnet run --project tests/MigrationChecks -c Release --no-build
pwsh -File scripts/Test-Api.ps1
```

On Windows:

```powershell
pwsh -File scripts/Test-Legacy.ps1
```

Explain the 2,000 deterministic invoice comparisons, the half-penny rounding boundary, and the concurrent stock check. The check runner compiles linked VB.NET business-rule source on .NET 8. The Windows script builds the .NET Framework desktop app in Debug and Release; compare the two interfaces by running them separately.

## 4:30 — Discuss the tradeoffs

“The file stores keep the sample easy to run. A multi-instance deployment would need database transactions. The profiles are synthetic pricing accounts; authentication and payments are outside this demo. My next steps would be .NET 10, a database, and a verified legacy XML importer.”

Open the GitHub workflow after publication. Only describe it as passing once the workflow has actually run successfully on GitHub.
