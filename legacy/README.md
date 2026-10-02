# Legacy order desk

This VB.NET / .NET Framework 4.8 baseline has two projects:

- `LegacyInvoices.Business`: pricing, seeded catalogue and customers, DataSet/XML orders, stock changes, reporting, and CSV export.
- `LegacyInvoices.Desktop`: Windows Forms interface for the order workflow.

Open `LegacyInvoices.sln` on Windows with Visual Studio 2022 and the .NET Framework 4.8 targeting pack. Set `LegacyInvoices.Desktop` as the startup project and press F5. Select a customer, add a catalogue product, review totals, and place an order. The desktop app shows a confirmation and saves the order under `data/orders.xml` beside its executable.

The desktop app can use a separate XML file through `LEGACY_DATA_FILE`. Run it before opening the modern dashboard to compare the same catalogue, quote, order, cancellation, and report workflow.

The rules are deliberately concentrated in a DataSet based store. That design provides a concrete baseline for the later C# migration and behavior comparisons.
