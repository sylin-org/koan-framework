using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.SqliteVec;
using Koan.Data.VectorAdapterSurface.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

/// <summary>Runs the shared Vector contract against the shipping embedded sqlite-vec provider.</summary>
public sealed class SqliteVecTestFactory : IVectorAdapterTestFactory
{
    private const string ConnectionString = "Data Source=:memory:";
    private ServiceProvider? _services;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IServiceProvider Services => _services ??= BuildProvider();
    public int EmbeddingDimension => 8;

    public bool SupportsGetEmbedding => true;
    public bool SupportsBulkOperations => true;
    public bool SupportsFlush => true;
    public bool SupportsExportAll => false;
    public bool SupportsIndexStats => false;
    public bool SupportsHybridSearch => false;
    public bool SupportsMetadataFilters => false;
    public bool SupportsContinuationToken => false;
    public bool SupportsPartitionIsolation => true;
    public bool SupportsDynamicCollections => true;
    public bool SupportsScoreNormalization => true;
    public bool SupportsDeleteImmediatelyVisibleToSearch => true;

    public ValueTask InitializeAsync()
    {
        _ = Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync().ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_services is not null)
            await _services.DisposeAsync().ConfigureAwait(false);

        _services = BuildProvider();
        Koan.Core.Hosting.App.AppHost.Current = _services;
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddVectorAdapterTestRuntime();
        services.AddSingleton<DataSourceRegistry>();
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddOptions<SqliteVecOptions>().Configure(options =>
        {
            options.ConnectionString = ConnectionString;
            options.DistanceMetric = "cosine";
        });
        services.AddSingleton<SqliteVecAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(provider =>
            provider.GetRequiredService<SqliteVecAdapterFactory>());
        return services.BuildServiceProvider();
    }
}
