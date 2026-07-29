using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
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
    private IHost? _host;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IServiceProvider Services
    {
        get
        {
            // Lazy init on first access — supports both spec base lifecycles (factory's own
            // IAsyncLifetime.InitializeAsync, and access from within a spec's InitializeAsync).
            if (_host is null) _host = BuildHost();
            return _host.Services;
        }
    }
    public int EmbeddingDimension => 8;

    // Capability declaration is deliberately narrow. The floor proves only the semantics it owns.
    public bool SupportsGetEmbedding         => true;
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;
    public bool SupportsExportAll            => false;
    public bool SupportsIndexStats           => false;
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => true;
    public bool SupportsContinuationToken    => false;
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;
    public bool SupportsScoreNormalization   => true;

    private IHost BuildHost()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Tenancy:Posture"] = "Open"
            }))
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("VectorTests").Vector<TodoVector>(space => space
                    .Name("TodosVector")
                    .Dimensions(EmbeddingDimension)
                    .Metric(VectorMetric.Cosine)
                    .Visibility(VectorVisibility.Session))))
            .Build();
        host.Start();
        return host;
    }

    public ValueTask InitializeAsync() { _ = Services; return ValueTask.CompletedTask; }

    public async ValueTask DisposeAsync()
    {
        if (_host is null) return;
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
        _host = null;
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_host is not null)
        {
            await _host.StopAsync(ct).ConfigureAwait(false);
            _host.Dispose();
            _host = null;
        }
        Koan.Core.Hosting.App.AppHost.Current = Services;
    }
}
