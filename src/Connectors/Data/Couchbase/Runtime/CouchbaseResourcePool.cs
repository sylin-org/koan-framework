using Couchbase;
using Couchbase.Core.IO.Authentication.Authenticators;
using Couchbase.Diagnostics;
using Couchbase.KeyValue;
using Koan.Data.Connector.Couchbase.Infrastructure;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseResourcePool : IAsyncDisposable, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ClusterKey, Lazy<Task<ClusterResource>>> _clusters = new();

    internal int ClusterCount
    {
        get { lock (_gate) return _clusters.Count; }
    }

    internal async Task<CouchbaseTarget> Target(CouchbaseRoute route, CancellationToken ct)
    {
        var cluster = await Cluster(route, ct).ConfigureAwait(false);
        var bucket = await cluster.Bucket(route.Bucket, ct).ConfigureAwait(false);
        return new CouchbaseTarget(cluster.Cluster, bucket);
    }

    internal async Task Probe(CouchbaseRoute route, CancellationToken ct)
    {
        var target = await Target(route, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        _ = await target.Cluster.PingAsync().ConfigureAwait(false);
    }

    private async Task<ClusterResource> Cluster(CouchbaseRoute route, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = new ClusterKey(route.ConnectionString, route.Username, route.Password);
        Lazy<Task<ClusterResource>> entry;
        lock (_gate)
        {
            if (!_clusters.TryGetValue(key, out entry!))
            {
                if (_clusters.Count >= Constants.MaximumRoutes)
                    throw new InvalidOperationException(
                        $"Couchbase reached the bounded cluster-route limit of {Constants.MaximumRoutes}.");
                entry = new Lazy<Task<ClusterResource>>(
                    () => Connect(key, route.Bucket, route.BootstrapTimeout, route.BootstrapPollInterval),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _clusters.Add(key, entry);
            }
        }

        try { return await entry.Value.WaitAsync(ct).ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if (_clusters.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _clusters.Remove(key);
            throw;
        }
    }

    private static async Task<ClusterResource> Connect(
        ClusterKey key,
        string initialBucket,
        TimeSpan bootstrapTimeout,
        TimeSpan pollInterval)
    {
        var options = new ClusterOptions { ConnectionString = key.ConnectionString };
        if (!string.IsNullOrWhiteSpace(key.Username))
            options.WithAuthenticator(new PasswordAuthenticator(key.Username, key.Password ?? ""));
        var cluster = await global::Couchbase.Cluster.ConnectAsync(options).ConfigureAwait(false);
        try
        {
            await cluster.WaitUntilReadyAsync(
                bootstrapTimeout,
                new WaitUntilReadyOptions().ServiceTypes(ServiceType.KeyValue, ServiceType.Query)).ConfigureAwait(false);
            var bucket = await cluster.BucketAsync(initialBucket).ConfigureAwait(false);
            await bucket.WaitUntilReadyAsync(
                bootstrapTimeout,
                new WaitUntilReadyOptions().ServiceTypes(ServiceType.KeyValue, ServiceType.Query)).ConfigureAwait(false);
            await WaitForQuery(cluster, bootstrapTimeout, pollInterval).ConfigureAwait(false);
            return new ClusterResource(cluster);
        }
        catch
        {
            cluster.Dispose();
            throw;
        }
    }

    private static async Task WaitForQuery(ICluster cluster, TimeSpan timeout, TimeSpan pollInterval)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var result = await cluster.QueryAsync<int>(
                    "SELECT RAW 1",
                    new global::Couchbase.Query.QueryOptions().Readonly(true).Timeout(TimeSpan.FromSeconds(5)))
                    .ConfigureAwait(false);
                await foreach (var value in result.Rows.ConfigureAwait(false))
                    if (value == 1) return;
            }
            catch (Exception error) when (error is ServiceNotAvailableException or TimeoutException)
            {
                last = error;
            }
            await Task.Delay(pollInterval).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Couchbase Query service did not become ready within {timeout}.",
            last);
    }

    public async ValueTask DisposeAsync()
    {
        Lazy<Task<ClusterResource>>[] entries;
        lock (_gate)
        {
            entries = _clusters.Values.ToArray();
            _clusters.Clear();
        }
        foreach (var entry in entries)
            if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
                await entry.Value.Result.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        Lazy<Task<ClusterResource>>[] entries;
        lock (_gate)
        {
            entries = _clusters.Values.ToArray();
            _clusters.Clear();
        }
        foreach (var entry in entries)
            if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
                entry.Value.Result.Dispose();
    }

    private readonly record struct ClusterKey(string ConnectionString, string? Username, string? Password);

    private sealed class ClusterResource(ICluster cluster) : IAsyncDisposable, IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Lazy<Task<IBucket>>> _buckets = new(StringComparer.Ordinal);

        internal ICluster Cluster { get; } = cluster;

        internal async Task<IBucket> Bucket(string name, CancellationToken ct)
        {
            Lazy<Task<IBucket>> entry;
            lock (_gate)
            {
                if (!_buckets.TryGetValue(name, out entry!))
                {
                    entry = new Lazy<Task<IBucket>>(
                        () => Cluster.BucketAsync(name).AsTask(),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    _buckets.Add(name, entry);
                }
            }
            return await entry.Value.WaitAsync(ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            Cluster.Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Cluster.Dispose();
    }
}

internal readonly record struct CouchbaseTarget(ICluster Cluster, IBucket Bucket);
