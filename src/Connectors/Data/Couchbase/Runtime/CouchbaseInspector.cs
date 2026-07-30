using System.Globalization;
using Couchbase.Query;
using Koan.Data.Abstractions;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseInspector(CouchbaseRoute route, CouchbaseResourcePool resources) : IDataSourceInspectorAdapter
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords;

    public async Task<SourceContainerBatch> Containers(
        int take,
        string? providerContinuation,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = Continuation(providerContinuation);
        var target = await resources.Target(route, ct).ConfigureAwait(false);
        var scopes = await target.Bucket.Collections.GetAllScopesAsync().ConfigureAwait(false);
        var all = scopes
            .SelectMany(scope => scope.Collections.Select(collection =>
                new CouchbaseContainer(scope.Name, collection.Name)))
            .OrderBy(static value => value.Scope, StringComparer.Ordinal)
            .ThenBy(static value => value.Collection, StringComparer.Ordinal)
            .Take(Infrastructure.Constants.MaximumContainersPerRoute + 1)
            .ToArray();
        var providerBound = all.Length > Infrastructure.Constants.MaximumContainersPerRoute;
        if (providerBound) all = all[..Infrastructure.Constants.MaximumContainersPerRoute];
        var values = all.Skip(offset).Take(take).Select(Descriptor).ToArray();
        var consumed = offset + values.Length;
        var more = consumed < all.Length;
        return new SourceContainerBatch(
            values,
            more ? StorageContainerPageCompletion.MoreAvailable :
            providerBound ? StorageContainerPageCompletion.ProviderLimit : StorageContainerPageCompletion.Complete,
            more ? consumed.ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default)
    {
        var container = Address(address);
        var target = await resources.Target(route, ct).ConfigureAwait(false);
        var scopes = await target.Bucket.Collections.GetAllScopesAsync().ConfigureAwait(false);
        var exists = scopes.Any(scope => string.Equals(scope.Name, container.Scope, StringComparison.Ordinal) &&
            scope.Collections.Any(collection => string.Equals(collection.Name, container.Collection, StringComparison.Ordinal)));
        if (!exists)
            throw new KeyNotFoundException(
                $"Couchbase container '{route.Bucket}/{container.Scope}/{container.Collection}' does not exist on source '{route.Source}'.");
        return Reference(container);
    }

    public Task<StorageContainerDescriptor> Describe(
        StorageContainerReference reference,
        CancellationToken ct = default) => Task.FromResult(Descriptor(Require(reference)));

    public async Task<INeutralRecordReader> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var container = Require(reference);
        var target = await resources.Target(route, ct).ConfigureAwait(false);
        var statement = $"SELECT RAW doc FROM {container.Qualified(route.Bucket)} AS doc LIMIT {checked(take + 1)}";
        var options = new QueryOptions().Readonly(true)
            .ScanConsistency(QueryScanConsistency.RequestPlus)
            .Timeout(route.QueryTimeout);
        var result = await target.Cluster.QueryAsync<JObject>(statement, options)
            .ConfigureAwait(false);
        var documents = new List<JObject>(take + 1);
        await foreach (var row in result.Rows.WithCancellation(ct).ConfigureAwait(false))
            documents.Add(row);
        return CouchbaseNeutralReader.Bounded(documents, take);
    }

    private StorageContainerDescriptor Descriptor(CouchbaseContainer container)
    {
        var reference = Reference(container);
        var address = reference.Address;
        return new StorageContainerDescriptor(
            reference,
            address,
            $"{route.Bucket}/{container.Scope}/{container.Collection}",
            "collection",
            StorageContainerTraits.Records | StorageContainerTraits.Physical,
            StorageContainerOperations.Describe | StorageContainerOperations.Sample |
            StorageContainerOperations.Query | StorageContainerOperations.Write);
    }

    private CouchbaseContainerReference Reference(CouchbaseContainer container) =>
        new(route.Source, StorageAddress.From(container.Scope, container.Collection));

    private CouchbaseContainer Require(StorageContainerReference reference)
    {
        if (reference is not CouchbaseContainerReference value ||
            !string.Equals(reference.Source, route.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(route.Source, reference.Source);
        return Address(value.Address);
    }

    private CouchbaseContainer Address(StorageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace.Count > 1)
            throw new KeyNotFoundException(
                $"Couchbase source '{route.Source}' accepts at most one namespace segment for scope.");
        return new CouchbaseContainer(
            address.Namespace.Count == 0 ? route.DefaultScope : address.Namespace[0],
            address.Name);
    }

    private static int Continuation(string? value) => string.IsNullOrWhiteSpace(value)
        ? 0
        : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset >= 0
            ? offset
            : throw new ArgumentException("Couchbase container continuation is invalid.", nameof(value));
}

internal sealed class CouchbaseContainerReference(string source, StorageAddress address)
    : StorageContainerReference(source, address);
