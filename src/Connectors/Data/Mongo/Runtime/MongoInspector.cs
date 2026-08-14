using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Mongo.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoInspector(MongoRoute route, MongoClientManager clients) :
    IDataSourceInspectorAdapter,
    IDataSourceStatusInspector
{
    public SourceInspectionCapabilities Capabilities =>
        SourceInspectionCapabilities.ListContainers |
        SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer |
        SourceInspectionCapabilities.SampleRecords;

    public IDataSourceNativeInspector Native => this;

    public async Task<DataSourceStorageState> Status(CancellationToken ct = default)
    {
        try
        {
            await clients.Ping(route, ct).ConfigureAwait(false);
            return new DataSourceStorageState(DataSourceStorageStatus.Ready, Constants.StorageStatus.Ready);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new DataSourceStorageState(DataSourceStorageStatus.Unavailable, Constants.StorageStatus.Timeout);
        }
        catch (MongoException)
        {
            return new DataSourceStorageState(DataSourceStorageStatus.Unavailable, Constants.StorageStatus.Unavailable);
        }
        catch
        {
            return new DataSourceStorageState(DataSourceStorageStatus.Unavailable, Constants.StorageStatus.Unavailable);
        }
    }

    public async Task<SourceContainerBatch> Containers(
        int take,
        string? providerContinuation,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var offset = ParseContinuation(providerContinuation);
        var all = new List<(string Name, string Kind)>();
        var providerBound = false;
        var database = await clients.Database(route, ct).ConfigureAwait(false);
        using var cursor = await database.ListCollectionsAsync(cancellationToken: ct)
            .ConfigureAwait(false);
        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
        {
            foreach (var value in cursor.Current)
            {
                if (all.Count == Constants.Provider.MaximumCollectionsPerRepository)
                {
                    providerBound = true;
                    break;
                }
                all.Add((value["name"].AsString, value.GetValue("type", "collection").AsString));
            }
            if (providerBound) break;
        }
        all.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var values = all.Skip(offset).Take(take).Select(item => Descriptor(item.Name, item.Kind)).ToArray();
        var consumed = offset + values.Length;
        var more = consumed < all.Count;
        var completion = more
            ? StorageContainerPageCompletion.MoreAvailable
            : providerBound
                ? StorageContainerPageCompletion.ProviderLimit
                : StorageContainerPageCompletion.Complete;
        return new SourceContainerBatch(
            values,
            completion,
            more ? consumed.ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default)
    {
        ValidateAddress(route, address);
        var kind = await Kind(address.Name, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"MongoDB container '{address}' does not exist on source '{route.Source}'.");
        return new MongoContainerReference(route.Source, Canonical(address.Name), kind);
    }

    public async Task<StorageContainerDescriptor> Describe(
        StorageContainerReference reference,
        CancellationToken ct = default)
    {
        var mongo = Require(reference);
        var kind = await Kind(mongo.Address.Name, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"MongoDB container '{mongo.Address}' no longer exists on source '{route.Source}'.");
        return Descriptor(mongo.Address.Name, kind);
    }

    public async Task<INeutralRecordReader> Sample(
        StorageContainerReference reference,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var mongo = Require(reference);
        var database = await clients.Database(route, ct).ConfigureAwait(false);
        var documents = await database
            .GetCollection<RawBsonDocument>(mongo.Address.Name)
            .Find(FilterDefinition<RawBsonDocument>.Empty)
            .Limit(checked(take + 1))
            .ToListAsync(ct).ConfigureAwait(false);
        try
        {
            return MongoNeutralReader.Bounded(documents, take);
        }
        finally
        {
            foreach (var document in documents) document.Dispose();
        }
    }

    internal static void ValidateAddress(MongoRoute route, StorageAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Namespace.Count > 1 ||
            address.Namespace.Count == 1 &&
            !string.Equals(address.Namespace[0], route.Database, StringComparison.Ordinal))
            throw new KeyNotFoundException(
                $"MongoDB source '{route.Source}' has no namespace '{string.Join('/', address.Namespace)}'.");
    }

    private StorageContainerDescriptor Descriptor(string name, string kind)
    {
        var address = Canonical(name);
        var reference = new MongoContainerReference(route.Source, address, kind);
        var view = string.Equals(kind, "view", StringComparison.OrdinalIgnoreCase);
        var readOnly = view || route.Access == DataSourceAccess.ReadOnly;
        return new StorageContainerDescriptor(
            reference,
            address,
            $"{route.Database}/{name}",
            kind,
            StorageContainerTraits.Records |
            (view ? StorageContainerTraits.Virtual : StorageContainerTraits.Physical) |
            (readOnly ? StorageContainerTraits.ReadOnly : StorageContainerTraits.None),
            StorageContainerOperations.Describe |
            StorageContainerOperations.Sample |
            StorageContainerOperations.Query |
            (readOnly ? StorageContainerOperations.None : StorageContainerOperations.Write));
    }

    private MongoContainerReference Require(StorageContainerReference reference)
    {
        if (reference is not MongoContainerReference mongo ||
            !string.Equals(reference.Source, route.Source, StringComparison.OrdinalIgnoreCase))
            throw new StorageReferenceSourceMismatchException(route.Source, reference.Source);
        return mongo;
    }

    private async Task<string?> Kind(string name, CancellationToken ct)
    {
        var database = await clients.Database(route, ct).ConfigureAwait(false);
        using var cursor = await database.ListCollectionsAsync(
                new ListCollectionsOptions { Filter = new BsonDocument("name", name) },
                ct)
            .ConfigureAwait(false);
        while (await cursor.MoveNextAsync(ct).ConfigureAwait(false))
            foreach (var value in cursor.Current)
                return value.GetValue("type", "collection").AsString;
        return null;
    }

    private StorageAddress Canonical(string name) => StorageAddress.From(route.Database, name);

    private static int ParseContinuation(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset >= 0
            ? offset
            : throw new ArgumentException("MongoDB container continuation is invalid.", nameof(value));
}

internal sealed class MongoContainerReference(
    string source,
    StorageAddress address,
    string kind) : StorageContainerReference(source, address)
{
    public string Kind { get; } = kind;
}
