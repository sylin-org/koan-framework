using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.OpenSearch.Tests;

/// <summary>
/// OpenSearch cell of the vector matrix — currently a "green-skip" stand-in.
///
/// <para>
/// The Koan OpenSearch vector adapter (<c>OpenSearchVectorRepository</c>) emits ES 8-style KNN
/// query syntax (<c>{"knn":{"field":"embedding","query_vector":[...]}}</c>), which OpenSearch 2.x
/// rejects with <c>parsing_exception: Unknown key for a START_OBJECT in [knn]</c>. OpenSearch
/// expects <c>{"knn":{"embedding":{"vector":[...],"k":N}}}</c> instead.
/// </para>
///
/// <para>
/// Wiring is preserved for the day the adapter is fixed: project layout, capability surface,
/// and spec subclasses are all in place. Flipping <see cref="IsAvailable"/> to a real container
/// check and re-introducing the BuildSp/ResetAsync bodies turns this back on.
/// </para>
/// </summary>
public sealed class OpenSearchTestFactory : IVectorAdapterTestFactory
{
    private const string AdapterBugReason =
        "OpenSearchVectorRepository emits ES-style KNN query syntax that OpenSearch 2.x rejects. "
        + "Tracked as adapter divergence — when the adapter emits OS-native KNN, flip IsAvailable.";

    public bool IsAvailable => false;
    public string? UnavailableReason => AdapterBugReason;
    public IServiceProvider Services => throw new InvalidOperationException(AdapterBugReason);
    public int EmbeddingDimension => 8;

    // Capability surface preserved for when the adapter is fixed.
    public bool SupportsGetEmbedding         => false;
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => false;
    public bool SupportsExportAll            => false;
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => true;
    public bool SupportsContinuationToken    => false;
    public bool SupportsPartitionIsolation   => false; // same per-(storage-name) cache bug as ES
    public bool SupportsDynamicCollections   => true;
    public bool SupportsScoreNormalization   => false;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
    public Task ResetAsync(CancellationToken ct = default) => Task.CompletedTask;
}
