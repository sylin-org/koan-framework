using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.VectorAdapterSurface.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

public sealed class SqliteVecTestFactory : IVectorAdapterTestFactory
{
    private IHost? _host;
    private string? _root;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public int EmbeddingDimension => 8;
    public bool SupportsExportAll => false;
    public bool SupportsIndexStats => false;
    public bool SupportsHybridSearch => false;
    public bool SupportsMetadataFilters => false;
    public bool SupportsContinuationToken => false;
    public bool SupportsScoreNormalization => true;

    public IServiceProvider Services
    {
        get
        {
            _host ??= BuildHost();
            return _host.Services;
        }
    }

    public ValueTask InitializeAsync()
    {
        _ = Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await Reset().ConfigureAwait(false);

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await Reset(ct).ConfigureAwait(false);
        AggregateConfigs.Reset();
        Koan.Core.Hosting.App.AppHost.Current = Services;
    }

    public async Task RestartPreservingStoreAsync(CancellationToken ct = default)
    {
        await StopHost(ct).ConfigureAwait(false);
        AggregateConfigs.Reset();
        Koan.Core.Hosting.App.AppHost.Current = Services;
    }

    private IHost BuildHost()
    {
        _root ??= Path.Combine(Path.GetTempPath(), "koan-sqlitevec-tests", Guid.NewGuid().ToString("N"));
        var connection = $"Data Source={Path.Combine(_root, "vectors.db")}";
        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Tenancy:Posture"] = "Open",
                ["Koan:Data:Sources:VectorTests:Adapter"] = "sqlitevec",
                ["Koan:Data:Sources:VectorTests:SqliteVec:ConnectionString"] = connection
            }))
            .ConfigureServices(services => services.AddKoan(koan =>
            {
                var source = koan.Data.Source("VectorTests");
                source.Vector<TodoVector>(space => Space(space, "todos", VectorMetric.Cosine));
                source.Vector<SqliteVecEuclideanDoc>(space => Space(space, "euclidean", VectorMetric.Euclidean));
                source.Vector<SqliteVecDotProductDoc>(space => Space(space, "dot", VectorMetric.DotProduct));
                source.Vector<SqliteVecDurableDoc>(space => Space(space, "durable", VectorMetric.Cosine));
                source.Vector<SqliteVecMetadataDoc>(space => Space(space, "metadata", VectorMetric.Cosine));
            }))
            .Build();
        host.Start();
        return host;
    }

    private async ValueTask Reset(CancellationToken ct = default)
    {
        await StopHost(ct).ConfigureAwait(false);
        if (_root is not null && Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
            _root = null;
        }
    }

    private async ValueTask StopHost(CancellationToken ct)
    {
        if (_host is null) return;
        await _host.StopAsync(ct).ConfigureAwait(false);
        _host.Dispose();
        _host = null;
    }

    private void Space<TEntity>(VectorSpaceBuilder<TEntity> space, string name, VectorMetric metric)
        where TEntity : class, Koan.Data.Abstractions.IEntity<string> => space
        .Name(name)
        .Dimensions(EmbeddingDimension)
        .Metric(metric)
        .Visibility(VectorVisibility.Session);
}
