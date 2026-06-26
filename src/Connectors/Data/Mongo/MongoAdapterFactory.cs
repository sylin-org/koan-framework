using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.Mongo;

[ProviderPriority(20)]
[KoanService(ServiceKind.Database, shortCode: "mongo", name: "MongoDB",
    ContainerImage = "mongo",
    DefaultTag = "7",
    DefaultPorts = new[] { 27017 },
    Capabilities = new[] { "protocol=mongodb" },
    Volumes = new[] { "./Data/mongo:/data/db" },
    AppEnv = new[] { "Koan__Data__Mongo__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Mongo__Database=Koan" },
    Scheme = "mongodb", Host = "mongo", EndpointPort = 27017, UriPattern = "mongodb://{host}:{port}",
    LocalScheme = "mongodb", LocalHost = "localhost", LocalPort = 27017, LocalPattern = "mongodb://{host}:{port}")]
public sealed class MongoAdapterFactory : IDataAdapterFactory, IAsyncDisposable
{
    // One MongoClientProvider (one MongoClient / connection pool) per resolved source — keyed by connection+database, so
    // every entity type on a source SHARES one pool instead of opening one per (entity, source). The Default source uses
    // the DI-managed provider (which also drives boot-time readiness); these cached providers are disposed with the
    // factory (a DI singleton).
    private readonly ConcurrentDictionary<string, MongoClientProvider> _sourceProviders = new(StringComparer.Ordinal);

    public string Provider => "mongo";

    public bool CanHandle(string provider) => string.Equals(provider, "mongo", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "mongodb", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var globalOptions = sp.GetRequiredService<IOptionsMonitor<MongoOptions>>();

        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            // Reuse the DI-managed provider so readiness monitoring and discovery outputs stay in sync.
            var sharedProvider = sp.GetRequiredService<MongoClientProvider>();
            return new MongoDocumentStore<TEntity, TKey>(sharedProvider, globalOptions, sp, "Default");
        }

        var baseOptions = globalOptions.CurrentValue;
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();

        // Resolve the source's physical placement (connection + database) — Database mode (ARCH-0103).
        var connectionString = AdapterConnectionResolver.ResolveConnectionString(config, sourceRegistry, "Mongo", source);
        if (string.IsNullOrWhiteSpace(connectionString)) connectionString = baseOptions.ConnectionString;
        var database = AdapterConnectionResolver.GetSourceSetting(config, sourceRegistry, "Mongo", source, "Database", baseOptions.Database);

        var sourceOptions = new MongoOptions
        {
            ConnectionString = connectionString,
            Database = database,
            DefaultPageSize = baseOptions.DefaultPageSize,
            Readiness = baseOptions.Readiness,
        };
        var optionsMonitor = new SimpleOptionsMonitor<MongoOptions>(sourceOptions);

        // One provider per (connection, database) — shared across every entity on this source.
        var provider = _sourceProviders.GetOrAdd(
            connectionString + "|" + database,
            _ => new MongoClientProvider(optionsMonitor, sp.GetService<ILogger<MongoClientProvider>>()));

        return new MongoDocumentStore<TEntity, TKey>(provider, optionsMonitor, sp, source);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _sourceProviders.Values)
            await provider.DisposeAsync().ConfigureAwait(false);
        _sourceProviders.Clear();
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = opts.NamingStyle,
            Separator = opts.Separator ?? ".",
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
            NameOverride = opts.CollectionName,
            // MongoDB's namespace (db.collection) limit is 255 bytes; reserve for the max 64-byte database
            // name + the '.' separator so the collection name alone is always valid. Without this, a very
            // long partition suffix would produce an over-limit collection name (StorageNameGenerator only
            // clamps when a limit is declared); with it, the overflow is hashed injectively instead.
            MaxIdentifierBytes = 255 - 64 - 1,
        };
    }
}

