using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

/// <summary>
/// Test factory for the InMemory cell of the vector matrix. Builds a minimal service provider
/// with <see cref="InMemoryVectorAdapterFactory"/> registered as the vector adapter; specs drive
/// <c>Vector&lt;TodoVector&gt;.*</c> through this provider via <c>AppHost.PushScope</c>.
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

    // Capability overrides — InMemory implements everything except hybrid search.
    public bool SupportsGetEmbedding         => true;
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;
    public bool SupportsExportAll            => true;
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => false; // matrix-side filter coverage not implemented for InMemory
    public bool SupportsContinuationToken    => false;
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

    public Task InitializeAsync() { _ = Services; return Task.CompletedTask; }

    public Task DisposeAsync()
    {
        _sp?.Dispose();
        return Task.CompletedTask;
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
