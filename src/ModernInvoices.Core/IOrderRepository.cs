namespace ModernInvoices.Core;

/// <summary>Reads operate on detached snapshots. Mutating a snapshot never changes committed state.</summary>
public interface IOrderReader
{
    T Read<T>(Func<StoreState, T> query);
}

/// <summary>
/// Within one repository instance, callbacks run serially on detached state.
/// A successful Update commits all changes; a throwing callback commits nothing and propagates its exception.
/// References returned or retained by a callback must not allow later mutation of committed state.
/// Callbacks must be synchronous, must not re-enter the repository, and must not retain work for another thread.
/// Multi-process transactions are outside this contract.
/// </summary>
public interface IOrderRepository : IOrderReader
{
    T Update<T>(Func<StoreState, T> transaction);
}

public sealed class StoreState
{
    public int SchemaVersion { get; set; } = 1;
    public List<Product> Products { get; set; } = [];
    public List<Customer> Customers { get; set; } = [];
    public List<Order> Orders { get; set; } = [];
}
