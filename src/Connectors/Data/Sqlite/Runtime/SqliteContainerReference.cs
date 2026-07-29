using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteContainerReference(string source, StorageAddress address, string kind)
    : StorageContainerReference(source, address)
{
    public string Kind { get; } = kind;
}
