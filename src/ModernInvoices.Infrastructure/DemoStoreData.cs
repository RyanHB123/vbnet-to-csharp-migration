using ModernInvoices.Core;

namespace ModernInvoices.Infrastructure;

public static class DemoStoreData
{
    public static StoreState Create() => new()
    {
        Products = [
            new("MON-001", "27-inch Studio Monitor", "Displays", 125m, 24),
            new("DOCK-002", "USB-C Desktop Dock", "Accessories", 49.5m, 40),
            new("KEY-003", "Mechanical Keyboard", "Accessories", 79.95m, 18),
            new("CAM-004", "HD Conference Camera", "Video", 89m, 8),
            new("ARM-005", "Adjustable Monitor Arm", "Workspace", 64.5m, 5),
            new("HUB-006", "7-Port USB Hub", "Accessories", 29.99m, 3)
        ],
        Customers = [
            new("CUST-001", "Northwind Studio", CustomerType.Trade),
            new("CUST-002", "Alex Morgan", CustomerType.Retail),
            new("CUST-003", "Harbour Design Co.", CustomerType.Trade)
        ]
    };
}
