using Koan.Data.Abstractions;

namespace Koan.Data.Relational;

public sealed class RelationalContainerReference(
    string source,
    StorageAddress address,
    string providerKind) : StorageContainerReference(source, address)
{
    public string ProviderKind { get; } = providerKind;
}
