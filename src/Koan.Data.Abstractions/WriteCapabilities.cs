namespace Koan.Data.Abstractions;

[Flags]
public enum WriteCapabilities
{
    None = 0,
    BulkUpsert = 1 << 0,
    BulkDelete = 1 << 1,
    AtomicBatch = 1 << 2,
}