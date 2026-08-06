namespace Koan.Data.Abstractions;

/// <summary>Provider-neutral, source-bound inspection surface.</summary>
public interface IDataSourceInspector
{
    Task<StorageContainerPage> Containers(
        int take,
        string? continuation = null,
        CancellationToken ct = default);

    Task<StorageContainerReference> Resolve(
        StorageAddress address,
        CancellationToken ct = default);

    Task<StorageContainerDescriptor> Describe(
        StorageContainerReference reference,
        CancellationToken ct = default);

    Task<RecordSet> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default);

    /// <summary>Returns an explicitly requested provider-native inspection capability, when available.</summary>
    TNative? As<TNative>() where TNative : class, IDataSourceNativeInspector;
}
