# Legacy order desk

This VB.NET / .NET Framework 4.8 baseline has three projects:

- `LegacyInvoices.Business`: pricing, seeded catalogue and customers, DataSet/XML orders, stock changes, reporting, and CSV export.
- `LegacyInvoices.Desktop`: Windows Forms interface for the order workflow.
- `LegacyInvoices`: interactive console interface over the same rules.

Open `LegacyInvoices.sln` on Windows with Visual Studio 2022 and the .NET Framework 4.8 targeting pack. Set `LegacyInvoices.Desktop` as the startup project and press F5. Select a customer, add a catalogue product, review totals, and place an order. The desktop app shows a confirmation and saves the order under `data/orders.xml` beside its executable.

The console app can be launched separately with `--interactive`. Try `products`, `customers`, `quote CUST-001 MON-001:8 DOCK-002:2`, `place CUST-001 MON-001:8 DOCK-002:2`, `orders`, and `report`. Use `LEGACY_DATA_FILE` to point either app at a separate XML file. Run them sequentially when sharing one file.

The rules are deliberately concentrated in a DataSet based store. That design provides a concrete baseline for the later C# migration and behavior comparisons.
