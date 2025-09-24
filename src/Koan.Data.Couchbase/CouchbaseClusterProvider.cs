using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.KeyValue;
using Koan.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Couchbase;

internal sealed class CouchbaseCollectionContext
{
    public CouchbaseCollectionContext(ICluster cluster, IBucket bucket, IScope scope, ICouchbaseCollection collection, string bucketName, string scopeName, string collectionName)
    {
        Cluster = cluster;
        Bucket = bucket;
        Scope = scope;
        Collection = collection;
        BucketName = bucketName;
        ScopeName = scopeName;
        CollectionName = collectionName;
    }

    public ICluster Cluster { get; }
    public IBucket Bucket { get; }
    public IScope Scope { get; }
    public ICouchbaseCollection Collection { get; }
    public string BucketName { get; }
    public string ScopeName { get; }
    public string CollectionName { get; }
}

internal sealed class CouchbaseClusterProvider : IAsyncDisposable
{
    private readonly IOptionsMonitor<CouchbaseOptions> _options;
    private readonly ILogger<CouchbaseClusterProvider>? _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private ICluster? _cluster;
    private IBucket? _bucket;
    private string? _bucketName;
    private readonly ConcurrentDictionary<string, IScope> _scopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICouchbaseCollection> _collections = new(StringComparer.OrdinalIgnoreCase);

    public CouchbaseClusterProvider(IOptionsMonitor<CouchbaseOptions> options, ILogger<CouchbaseClusterProvider>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<CouchbaseCollectionContext> GetCollectionContextAsync(string collectionName, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var cluster = await EnsureClusterAsync(options, ct).ConfigureAwait(false);
        var bucket = await EnsureBucketAsync(cluster, options, ct).ConfigureAwait(false);
        var scopeName = string.IsNullOrWhiteSpace(options.Scope) ? "_default" : options.Scope!;
        var scope = await GetScopeAsync(bucket, scopeName).ConfigureAwait(false);
        var finalCollection = string.IsNullOrWhiteSpace(collectionName)
            ? (!string.IsNullOrWhiteSpace(options.Collection) ? options.Collection! : "_default")
            : collectionName;
        var collection = await GetCollectionAsync(scope, scopeName, finalCollection).ConfigureAwait(false);
        return new CouchbaseCollectionContext(cluster, bucket, scope, collection, bucket.Name, scopeName, finalCollection);
    }

    private async ValueTask<ICluster> EnsureClusterAsync(CouchbaseOptions options, CancellationToken ct)
    {
        if (_cluster is not null)
        {
            return _cluster;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cluster is null)
            {
                var clusterOptions = new Couchbase.ClusterOptions
                {
                    UserName = options.Username ?? string.Empty,
                    Password = options.Password ?? string.Empty
                };

                if (string.IsNullOrWhiteSpace(clusterOptions.UserName) || string.IsNullOrWhiteSpace(clusterOptions.Password))
                {
                    clusterOptions = new Couchbase.ClusterOptions();
                    if (!string.IsNullOrWhiteSpace(options.Username))
                    {
                        clusterOptions.UserName = options.Username;
                        clusterOptions.Password = options.Password ?? string.Empty;
                    }
                }

                _logger?.LogDebug("Connecting to Couchbase cluster at {ConnectionString}", Redaction.DeIdentify(options.ConnectionString));
                _cluster = await Cluster.ConnectAsync(options.ConnectionString, clusterOptions).ConfigureAwait(false);
            }
        }
        finally
        {
            _sync.Release();
        }

        return _cluster!;
    }

    private async ValueTask<IBucket> EnsureBucketAsync(ICluster cluster, CouchbaseOptions options, CancellationToken ct)
    {
        if (_bucket is not null && string.Equals(_bucketName, options.Bucket, StringComparison.OrdinalIgnoreCase))
        {
            return _bucket;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_bucket is null || !string.Equals(_bucketName, options.Bucket, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Opening Couchbase bucket {Bucket}", options.Bucket);
                _bucket = await cluster.BucketAsync(options.Bucket).ConfigureAwait(false);
                _bucketName = options.Bucket;
                _scopes.Clear();
                _collections.Clear();
            }
        }
        finally
        {
            _sync.Release();
        }

        return _bucket!;
    }

    private async ValueTask<IScope> GetScopeAsync(IBucket bucket, string scopeName)
    {
        if (_scopes.TryGetValue(scopeName, out var cached))
        {
            return cached;
        }

        IScope scope;
        if (string.Equals(scopeName, "_default", StringComparison.Ordinal))
        {
            scope = bucket.Scope("_default");
        }
        else
        {
            scope = await bucket.ScopeAsync(scopeName).ConfigureAwait(false);
        }

        _scopes[scopeName] = scope;
        return scope;
    }

    private async ValueTask<ICouchbaseCollection> GetCollectionAsync(IScope scope, string scopeName, string collectionName)
    {
        var key = $"{scopeName}:{collectionName}";
        if (_collections.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ICouchbaseCollection collection;
        if (string.Equals(collectionName, "_default", StringComparison.Ordinal))
        {
            collection = scope.Collection("_default");
        }
        else
        {
            collection = await scope.CollectionAsync(collectionName).ConfigureAwait(false);
        }

        _collections[key] = collection;
        return collection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cluster is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _cluster?.Dispose();
        }
    }
}
