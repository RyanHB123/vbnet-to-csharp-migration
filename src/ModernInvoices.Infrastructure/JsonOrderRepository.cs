using System.Text.Json;
using System.Text.Json.Serialization;
using ModernInvoices.Core;

namespace ModernInvoices.Infrastructure;

// One application process per data file. A database is needed for multiple writers.
public sealed class JsonOrderRepository : IOrderRepository
{
    private readonly string path;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonOrderRepository(string path)
    {
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
    }

    private StoreState Load()
    {
        if (!File.Exists(path)) return DemoStoreData.Create();
        var state = JsonSerializer.Deserialize<StoreState>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Order data is empty.");
        if (state.SchemaVersion != 1) throw new InvalidDataException("Unsupported order data version.");
        return state;
    }

    public T Read<T>(Func<StoreState, T> query)
    {
        lock (gate) return query(Load());
    }

    public T Update<T>(Func<StoreState, T> transaction)
    {
        lock (gate)
        {
            var state = Load();
            var result = transaction(state);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return result;
        }
    }
}
