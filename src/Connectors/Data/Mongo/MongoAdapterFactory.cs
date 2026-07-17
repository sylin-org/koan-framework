using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
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
public sealed class MongoAdapterFactory : IDataAdapterFactory, IAsyncDisposable, IDisposable
{
    // One MongoClientProvider (one MongoClient / connection pool) per resolved source — keyed by connection+database, so
    // every entity type on a source SHARES one pool instead of opening one per (entity, source). The Default source uses
    // the DI-managed provider (which also drives lazy readiness); a routed source that physically coincides with
    // Default reuses that same DI provider (the dedup below) rather than opening a duplicate client.
    //
    // Lifetime (ARCH-0103 §9.15): each cached provider lives for the factory's lifetime (the factory is a DI singleton
    // disposed on host teardown). There is deliberately NO eviction: the repositories created here are themselves cached
    // upstream per (entity, key, adapter, source) and HOLD their provider, so evicting+disposing a cached provider would
    // kill a live repository's client. The cache is therefore bounded by the deployment's distinct-physical-source count,
    // not by entity count — one MongoClient per distinct (connection, database), which is the intended pooling contract.
    private readonly ConcurrentDictionary<string, MongoClientProvider> _sourceProviders = new(StringComparer.Ordinal);

    /// <summary>Test-support: the number of distinct non-Default per-source providers currently pooled (the Default and
    /// any Default-coinciding source reuse the DI-managed provider and are NOT counted here).</summary>
    internal int SourceProviderCount => _sourceProviders.Count;

    public string Provider => "mongo";
    public IReadOnlyCollection<string> Aliases => ["mongodb"];

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(sp, source);
        return new MongoDocumentStore<TEntity, TKey>(route.Provider, route.Options, sp, route.Source);
    }

    internal MongoSourceRoute ResolveRoute(IServiceProvider sp, string source)
    {
        var globalOptions = sp.GetRequiredService<IOptionsMonitor<MongoOptions>>();

        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            // Reuse the DI-managed provider so readiness monitoring and discovery outputs stay in sync.
            var sharedProvider = sp.GetRequiredService<MongoClientProvider>();
            return new MongoSourceRoute(sharedProvider, globalOptions, "Default");
        }

        var baseOptions = globalOptions.CurrentValue;
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();

        // Resolve the source's physical placement (connection + database) — Database mode (ARCH-0103). The shared
        // resolver collapses a non-Default source's "auto"/blank discovery sentinel onto the discovery-resolved Default
        // (so the per-source pool never keys on the unresolved literal) — the fleet hoist of the local
        // MongoConnectionString.ResolveRoutedConnection, which stays as the test-pinned pure 2-arg helper.
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            config, sourceRegistry, "Mongo", source, baseOptions.ConnectionString, this);
        var database = AdapterConnectionResolver.GetSourceSetting(
            config, sourceRegistry, "Mongo", source, "Database", baseOptions.Database, this);

        // Dedup (ARCH-0103 §9.15): a routed source whose resolved physical placement (connection + database) coincides
        // with Default — e.g. a source that relies on discovery, so ResolveRoutedConnection collapsed its sentinel onto
        // the Default connection — reuses the DI-managed provider (one MongoClient/pool + shared readiness) and Default's
        // full options (globalOptions), instead of opening a SECOND client to the same server+db keyed under a different
        // cache entry. Reusing globalOptions also sidesteps the partial-copy naming gap of the per-source path below
        // (sourceOptions copies only ConnectionString/Database/DefaultPageSize/Readiness, not NamingStyle/Separator/etc).
        if (!string.IsNullOrWhiteSpace(baseOptions.ConnectionString)
            && string.Equals(connectionString, baseOptions.ConnectionString, StringComparison.Ordinal)
            && string.Equals(database, baseOptions.Database, StringComparison.Ordinal))
        {
            var sharedProvider = sp.GetRequiredService<MongoClientProvider>();
            return new MongoSourceRoute(sharedProvider, globalOptions, source);
        }

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

        return new MongoSourceRoute(provider, optionsMonitor, source);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _sourceProviders.Values)
            await provider.DisposeAsync().ConfigureAwait(false);
        _sourceProviders.Clear();
    }

    // The factory is a DI singleton; the host disposes it async on teardown. Implement IDisposable too so a SYNC
    // ServiceProvider.Dispose() (future CLI/preflight tooling) doesn't throw on this IAsyncDisposable-only singleton.
    // MongoClientProvider.DisposeAsync is synchronous-bodied, so the bridge never blocks.
    public void Dispose()
    {
        foreach (var provider in _sourceProviders.Values)
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

internal sealed record MongoSourceRoute(
    MongoClientProvider Provider,
    IOptionsMonitor<MongoOptions> Options,
    string Source);

