using Koan.Core.Capabilities;
using Koan.Core.Services;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.Mongo.Infrastructure;
using Koan.Data.Connector.Mongo.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo;

[ProviderPriority(Constants.Provider.Priority)]
[KoanService(ServiceKind.Database, shortCode: "mongo", name: "MongoDB",
    ContainerImage = "mongo", DefaultTag = "8.3.4", DefaultPorts = [27017],
    Capabilities = ["protocol=mongodb"], Volumes = ["./Data/mongo-8.3:/data/db"],
    AppEnv = ["Koan__Data__Mongo__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Mongo__Database=Koan"],
    Scheme = "mongodb", Host = "mongo", EndpointPort = 27017, UriPattern = "mongodb://{host}:{port}",
    LocalScheme = "mongodb", LocalHost = "localhost", LocalPort = 27017, LocalPattern = "mongodb://{host}:{port}")]
public sealed class MongoAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory, IDataAdapterSetup
{
    public string Provider => Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => [Constants.Provider.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Mongo"];

    internal static bool HandlesProvider(string provider) =>
        string.Equals(provider, Constants.Provider.Name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, Constants.Provider.Alias, StringComparison.OrdinalIgnoreCase);

    public void DescribeClaims(IDataClaims claims) => MongoFeatures.Declare(claims);

    public DataAdapterSetupDescriptor DescribeSetup() => new("MongoDB",
    [
        new(nameof(MongoOptions.ConnectionString), "Connection string", DataProviderSetupFieldKind.ConnectionString,
            Placeholder: "mongodb://localhost:27017"),
        new(nameof(MongoOptions.Database), "Database", DataProviderSetupFieldKind.Text,
            DefaultValue: new MongoOptions().Database)
    ]);

    public async Task<DataProviderProbeResult> Probe(
        IServiceProvider services, DataProviderProbeContext candidate, CancellationToken ct = default)
    {
        var connectionString = candidate.Require(nameof(MongoOptions.ConnectionString));
        if (connectionString.Equals("auto", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("MongoDB candidate setup requires a concrete connection string.");
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(candidate.Require(nameof(MongoOptions.Database)));
        _ = await database.RunCommandAsync<BsonDocument>(
            new BsonDocumentCommand<BsonDocument>(new BsonDocument("ping", 1)),
            cancellationToken: ct).ConfigureAwait(false);
        return DataProviderProbeResult.Ready("The MongoDB database answered a read-only ping.");
    }

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers |
        SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer |
        SourceInspectionCapabilities.SampleRecords,
        ["pipeline"]);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source) =>
        new MongoSourceIntegration(
            ResolveRoute(services, source),
            services.GetRequiredService<MongoClientManager>());

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        var mapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(route.Source);
        return new MongoRepository<TEntity, TKey>(
            services,
            this,
            route,
            services.GetRequiredService<MongoClientManager>(),
            mapping);
    }

    /// <summary>
    /// The servers and the routed database: the parts that place a collection. Credentials and driver options do
    /// not, so two spellings of one deployment compare equal. MongoDB refuses database names that differ only by
    /// case, so the database compares without case. An unparsable connection is reduced as a whole.
    /// </summary>
    public string? ResolveStoreIdentity(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        string servers;
        try
        {
            servers = string.Join(",", MongoUrl.Create(route.ConnectionString).Servers
                .Select(static server => $"{server.Host}:{server.Port}".ToLowerInvariant())
                .Order(StringComparer.Ordinal));
        }
        catch (Exception error) when (error is MongoConfigurationException or ArgumentException or FormatException)
        {
            servers = DataSourceRegistry.IdentityOf(route.ConnectionString);
        }
        return DataSourceRegistry.IdentityOf($"{servers}\n{route.Database.ToLowerInvariant()}");
    }

    /// <summary>
    /// Asks each deployment who answered (<c>hello</c>, which needs no privilege): two writable primaries with different
    /// process ids are different deployments; one process is one deployment, whose databases differ only by name. A
    /// router (<c>mongos</c>) or a member that is not the writable primary is not proof, because two of them can front
    /// one data set. Nothing is written (NK-305).
    /// </summary>
    public async ValueTask<bool> ProveDistinctStores(
        IServiceProvider services,
        string source,
        string other,
        CancellationToken ct = default)
    {
        var first = ResolveRoute(services, source);
        var second = ResolveRoute(services, other);
        var clients = services.GetRequiredService<MongoClientManager>();
        var a = await Hello(first).ConfigureAwait(false);
        var b = await Hello(second).ConfigureAwait(false);
        if (a is null || b is null) return false;
        if (a != b) return true;
        return !string.Equals(first.Database, second.Database, StringComparison.OrdinalIgnoreCase);

        async Task<ObjectId?> Hello(MongoRoute route)
        {
            var database = await clients.Database(route, ct).ConfigureAwait(false);
            var reply = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("hello", 1), ReadPreference.Primary, ct).ConfigureAwait(false);
            if (reply.TryGetValue("msg", out var message) && message == "isdbgrid") return null;
            if (!reply.TryGetValue("isWritablePrimary", out var writable) || !writable.ToBoolean()) return null;
            return reply.TryGetValue("topologyVersion", out var topology) && topology is BsonDocument version &&
                   version.TryGetValue("processId", out var process) && process.IsObjectId
                ? process.AsObjectId
                : null;
        }
    }

    /// <summary>
    /// The id in the reserved <c>__koan_store</c> collection of the route's database (DATA-0148). It is created only where
    /// the source may create a collection (Managed + ReadWrite); a ReadOnly or External source reads it and never creates
    /// it, and a read never creates the collection.
    /// </summary>
    public async ValueTask<string?> ReadStoreMarker(
        IServiceProvider services,
        string source,
        bool create,
        CancellationToken ct = default)
    {
        var route = ResolveRoute(services, source);
        var database = await services.GetRequiredService<MongoClientManager>().Database(route, ct).ConfigureAwait(false);
        var markers = database.GetCollection<BsonDocument>(StoreMarker.Name);
        var slot = Builders<BsonDocument>.Filter.Eq("_id", Constants.Provider.StoreMarkerSlot);
        if (create && route.StorageLifecycle == StorageLifecycle.Managed && route.Access == DataSourceAccess.ReadWrite)
        {
            try
            {
                await markers.UpdateOneAsync(
                        slot,
                        Builders<BsonDocument>.Update.SetOnInsert("id", StoreMarker.NewId()),
                        new UpdateOptions { IsUpsert = true },
                        ct)
                    .ConfigureAwait(false);
            }
            catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another writer inserted the slot between the match and the insert; its id is the marker.
            }
        }

        var marker = await markers.Find(slot).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return marker is not null && marker.TryGetValue("id", out var id) && id.IsString ? id.AsString : null;
    }

    internal MongoRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var defaults = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            registry,
            Provider,
            resolvedSource,
            defaults.ConnectionString,
            this);
        var database = AdapterConnectionResolver.GetSourceSetting(
            configuration,
            registry,
            Provider,
            resolvedSource,
            "Database",
            defaults.Database,
            this);
        var definition = registry.GetSource(resolvedSource);
        return new MongoRoute(
            resolvedSource,
            connection,
            database,
            definition?.StorageLifecycle ?? StorageLifecycle.Managed,
            definition?.Access ?? DataSourceAccess.ReadWrite);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
            NameOverride = options.CollectionName,
            MaxIdentifierBytes = 190
        };
    }
}

internal sealed record MongoRoute(
    string Source,
    string ConnectionString,
    string Database,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access);
