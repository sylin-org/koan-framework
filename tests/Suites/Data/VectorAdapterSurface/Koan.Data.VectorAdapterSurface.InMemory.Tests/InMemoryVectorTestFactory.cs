using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.InMemory;
using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

/// <summary>
/// Test factory for the InMemory cell of the vector matrix. Builds a minimal service provider with the
/// SHIPPING <see cref="InMemoryVectorAdapterFactory"/> (Koan.Data.Vector.Connector.InMemory) registered as
/// the vector adapter; specs drive <c>Vector&lt;TodoVector&gt;.*</c> through this provider via
/// <c>AppHost.PushScope</c>. The shipping adapter IS the cross-adapter convergence oracle, so the matrix
/// validates every native provider against the same code that ships as the in-process vector floor.
/// </summary>
public sealed class InMemoryVectorTestFactory : IVectorAdapterTestFactory
{
    private readonly InMemoryVectorAdapterFactory _adapter = new();
    private ServiceProvider? _sp;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IServiceProvider Services
    {
        get
        {
            // Lazy init on first access — supports both spec base lifecycles (factory's own
            // IAsyncLifetime.InitializeAsync, and access from within a spec's InitializeAsync).
            if (_sp is null) _sp = BuildProvider();
            return _sp;
        }
    }
    public int EmbeddingDimension => 8;

    // Capability overrides — the in-memory reference implements every capability it can model
    // in-process (AI-0036 §9). Multi-vector-per-entity and atomic-batch are honestly omitted (a
    // single-vector, non-transactional dictionary cannot model them).
    public bool SupportsGetEmbedding         => true;
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;
    public bool SupportsExportAll            => true;
    public bool SupportsHybridSearch         => true;  // vector+keyword blend (Alpha + SearchText)
    public bool SupportsMetadataFilters      => true;  // unified Filter via DictionaryFilterEvaluator (the oracle)
    public bool SupportsContinuationToken    => true;  // offset-based paging
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;
    public bool SupportsScoreNormalization   => true;

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddKoanDataVector();
        services.AddSingleton<IVectorAdapterFactory>(_adapter);
        return services.BuildServiceProvider();
    }

    public ValueTask InitializeAsync() { _ = Services; return ValueTask.CompletedTask; }

    public ValueTask DisposeAsync()
    {
        _sp?.Dispose();
        return ValueTask.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        // InMemory only needs to clear data — the adapter has no schema/index cache to invalidate.
        // The data matrix InMemoryAdapterFactory pattern of setting AppHost.Current = Services
        // gives a global fallback for AsyncLocal-flow gaps; we do the same.
        Koan.Core.Hosting.App.AppHost.Current = Services;
        _adapter.ClearAll();
        return Task.CompletedTask;
    }
}
