namespace Sora.Data.Abstractions;

[Flags]
public enum QueryCapabilities
{
    None = 0,
    String = 1 << 0,
    Linq = 1 << 1,
}

public interface IQueryCapabilities
{
    QueryCapabilities Capabilities { get; }
}

// Declared provider capabilities for write paths
[Flags]
public enum WriteCapabilities
{
    None = 0,
    BulkUpsert = 1 << 0,
    BulkDelete = 1 << 1,
    AtomicBatch = 1 << 2,
}

public interface IWriteCapabilities
{
    WriteCapabilities Writes { get; }
}

// Optional marker interfaces that providers can implement to indicate native bulk semantics
public interface IBulkDelete<TKey> where TKey : notnull { }
public interface IBulkUpsert<TKey> where TKey : notnull { }
