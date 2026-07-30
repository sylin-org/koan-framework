using Couchbase;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseSchema(CouchbaseRoute route, CouchbaseResourcePool resources)
{
    private readonly object _gate = new();
    private readonly Dictionary<(CouchbaseContainer Container, bool Queryable), Lazy<Task>> _entries = new();

    internal Task Ensure(CouchbaseContainer container, bool queryable, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = (container, queryable);
        Lazy<Task> entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                if (_entries.Count >= Infrastructure.Constants.MaximumContainersPerRoute * 2)
                    throw new InvalidOperationException(
                        $"Couchbase reached the bounded schema-plan limit for '{typeof(CouchbaseSchema).Name}'.");
                entry = new Lazy<Task>(
                    () => EnsureCore(container, queryable, ct),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _entries.Add(key, entry);
            }
        }
        return Observe(key, entry);
    }

    private async Task Observe((CouchbaseContainer, bool) key, Lazy<Task> entry)
    {
        try { await entry.Value.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(key);
            throw;
        }
    }

    private async Task EnsureCore(CouchbaseContainer container, bool queryable, CancellationToken ct)
    {
        var target = await resources.Target(route, ct).ConfigureAwait(false);
        var scopes = await target.Bucket.Collections.GetAllScopesAsync().ConfigureAwait(false);
        var scope = scopes.SingleOrDefault(value => string.Equals(value.Name, container.Scope, StringComparison.Ordinal));
        var exists = scope?.Collections.Any(value =>
            string.Equals(value.Name, container.Collection, StringComparison.Ordinal)) == true;

        if (!exists && route.StorageLifecycle == StorageLifecycle.External)
            throw new InvalidOperationException(
                $"External Couchbase container '{route.Bucket}/{container.Scope}/{container.Collection}' does not exist. " +
                "Create it outside Koan or select StorageLifecycle=Managed.");

        if (!exists)
        {
            route.Plan.Demand(DataOperationEffect.SchemaOrAdmin, "create Couchbase scope/collection");
            if (scope is null)
                try { await target.Bucket.Collections.CreateScopeAsync(container.Scope).ConfigureAwait(false); }
                catch (ScopeExistsException) { }
            try
            {
                await target.Bucket.Collections.CreateCollectionAsync(
                    container.Scope,
                    container.Collection,
                    new CreateCollectionSettings()).ConfigureAwait(false);
            }
            catch (CollectionExistsException) { }
        }

        if (!queryable) return;
        if (route.StorageLifecycle == StorageLifecycle.External) return;
        route.Plan.Demand(DataOperationEffect.SchemaOrAdmin, "create Couchbase query index");
        var statement = $"CREATE PRIMARY INDEX IF NOT EXISTS ON {container.Qualified(route.Bucket)} USING GSI";
        var result = await target.Cluster.QueryAsync<dynamic>(statement, options =>
            options.Readonly(false).Timeout(route.QueryTimeout)).ConfigureAwait(false);
        await foreach (var _ in result.Rows.WithCancellation(ct).ConfigureAwait(false)) { }
    }
}
