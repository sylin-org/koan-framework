namespace Koan.Data.Abstractions;

/// <summary>Native inspection mechanics. Data validates source binding, policy, bounds, and neutral results.</summary>
public interface IDataSourceInspectorAdapter
{
    SourceInspectionCapabilities Capabilities { get; }
    IDataSourceNativeInspector? Native => null;

    Task<SourceContainerBatch> Containers(
        int take,
        string? providerContinuation,
        CancellationToken ct = default);

    Task<StorageContainerReference> Resolve(
        StorageAddress address,
        CancellationToken ct = default);

    Task<StorageContainerDescriptor> Describe(
        StorageContainerReference reference,
        CancellationToken ct = default);

    Task<INeutralRecordReader> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default);
}
