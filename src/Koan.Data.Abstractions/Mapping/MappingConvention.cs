namespace Koan.Data.Abstractions;

/// <summary>A provider's explicit managed-store default when the application declares no external-shape map.</summary>
public sealed record MappingConvention
{
    public MappingConvention(StorageAddress container, string keyName, string objectName)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        Container = container;
        KeyName = keyName.Trim();
        ObjectName = objectName.Trim();
    }

    public StorageAddress Container { get; }
    public string KeyName { get; }
    public string ObjectName { get; }
}
