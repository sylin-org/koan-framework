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
    private readonly Lazy<ServiceProvider> _sp;

    public InMemoryVectorTestFactory()
    {
        _sp = new Lazy<ServiceProvider>(BuildProvider, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IServiceProvider Services => _sp.Value;
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
        if (_sp.IsValueCreated) _sp.Value.Dispose();
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        // Mirror the data matrix InMemoryAdapterFactory pattern: also set the static global so
        // tests that get scheduled on threads where the AsyncLocal hasn't flowed still resolve
        // the correct provider. The PushScope in the spec base remains the primary signal.
        Koan.Core.Hosting.App.AppHost.Current = Services;
        _adapter.ClearAll();
        return Task.CompletedTask;
    }
}
