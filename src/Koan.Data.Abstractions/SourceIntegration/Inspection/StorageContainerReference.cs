namespace Koan.Data.Abstractions;

/// <summary>Opaque provider-issued execution reference bound to one logical source.</summary>
public abstract class StorageContainerReference
{
    protected StorageContainerReference(string source, StorageAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(address);
        Source = source;
        Address = address;
    }

    public string Source { get; }
    public StorageAddress Address { get; }
}
