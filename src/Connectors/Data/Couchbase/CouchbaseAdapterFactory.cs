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

namespace Koan.Data.Connector.Couchbase;

[ProviderPriority(30)]
[KoanService(ServiceKind.Database, shortCode: "couchbase", name: "Couchbase",
    ContainerImage = "couchbase/server",
    DefaultTag = "latest",
    DefaultPorts = new[] { 8091, 8092, 8093, 8094, 11210 },
    Capabilities = new[] { "protocol=couchbase" },
    Volumes = new[] { "./Data/couchbase:/opt/couchbase/var" },
    AppEnv = new[] { "Koan__Data__Couchbase__ConnectionString=couchbase://{host}", "Koan__Data__Couchbase__Bucket=Koan" },
    Scheme = "couchbase", Host = "couchbase", EndpointPort = 8091,
    UriPattern = "couchbase://{host}", LocalScheme = "couchbase", LocalHost = "localhost", LocalPort = 8091, LocalPattern = "couchbase://{host}")]
public sealed class CouchbaseAdapterFactory : IDataAdapterFactory, IAsyncDisposable, IDisposable
{
    // One CouchbaseClusterProvider (one cluster connection + one bucket) per resolved source — keyed by connection+bucket,
    // so every entity type on a source SHARES one provider instead of opening one per (entity, source). The Default source
    // uses the DI-managed provider (which also drives boot-time readiness); a routed source that physically coincides with
    // Default reuses that same DI provider (the dedup below) rather than opening a duplicate cluster connection. Database
    // mode (ARCH-0103) = a distinct native BUCKET per routed source.
    //
    // Lifetime (ARCH-0103 §9.15): each cached provider lives for the factory's lifetime (a DI singleton disposed on host
    // teardown). There is deliberately NO eviction: the repositories created here are cached upstream per (entity, key,
    // adapter, source) and HOLD their provider, so evicting+disposing one would kill a live repository's connection. The
    // cache is bounded by the deployment's distinct-physical-source count, not by entity count.
    private readonly ConcurrentDictionary<(string Connection, string Bucket, string? Username, string? Password, string? ManagementUrl), CouchbaseClusterProvider> _sourceProviders = new();

    /// <summary>Test-support: the number of distinct non-Default per-source providers currently pooled (the Default and
    /// any Default-coinciding source reuse the DI-managed provider and are NOT counted here).</summary>
    internal int SourceProviderCount => _sourceProviders.Count;

    public string Provider => "couchbase";

    public bool CanHandle(string provider)
        => string.Equals(provider, "couchbase", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var globalOptions = sp.GetRequiredService<IOptionsMonitor<CouchbaseOptions>>();

        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
        {
            // Reuse the DI-managed provider so readiness monitoring and discovery outputs stay in sync.
            var sharedProvider = sp.GetRequiredService<CouchbaseClusterProvider>();
            return new CouchbaseDocumentStore<TEntity, TKey>(sharedProvider, globalOptions, sp, "Default");
        }

        var baseOptions = globalOptions.CurrentValue;
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();

        // Resolve the source's physical placement (connection + bucket) — Database mode (ARCH-0103). The shared resolver
        // collapses a non-Default source's "auto"/blank discovery sentinel onto the discovery-resolved Default (so the
        // per-source pool never keys on the unresolved literal) — the fleet form of the local helper this replaces.
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            config, sourceRegistry, "Couchbase", source, baseOptions.ConnectionString, CanHandle);
        var bucket = AdapterConnectionResolver.GetSourceSetting(
            config, sourceRegistry, "Couchbase", source, "Bucket", baseOptions.Bucket, CanHandle);
        var username = NullIfBlank(AdapterConnectionResolver.GetSourceSetting(
            config, sourceRegistry, "Couchbase", source, "Username", baseOptions.Username ?? "", CanHandle));
        var password = NullIfBlank(AdapterConnectionResolver.GetSourceSetting(
            config, sourceRegistry, "Couchbase", source, "Password", baseOptions.Password ?? "", CanHandle));
        var managementUrl = NullIfBlank(AdapterConnectionResolver.GetSourceSetting(
            config, sourceRegistry, "Couchbase", source, "ManagementUrl", baseOptions.ManagementUrl ?? "", CanHandle));

        // Dedup (ARCH-0103 §9.15): a routed source whose resolved physical placement coincides with Default — same
        // connection, bucket, AND credentials/management endpoint (so reusing Default's provider can't cross to a
        // different cluster identity) — reuses the DI-managed provider (one cluster connection + shared readiness) and
        // Default's full options, instead of opening a duplicate keyed under a different cache entry.
        if (!string.IsNullOrWhiteSpace(baseOptions.ConnectionString)
            && string.Equals(connectionString, baseOptions.ConnectionString, StringComparison.Ordinal)
            && string.Equals(bucket, baseOptions.Bucket, StringComparison.Ordinal)
            && string.Equals(username, NullIfBlank(baseOptions.Username), StringComparison.Ordinal)
            && string.Equals(password, NullIfBlank(baseOptions.Password), StringComparison.Ordinal)
            && string.Equals(managementUrl, NullIfBlank(baseOptions.ManagementUrl), StringComparison.Ordinal))
        {
            var sharedProvider = sp.GetRequiredService<CouchbaseClusterProvider>();
            return new CouchbaseDocumentStore<TEntity, TKey>(sharedProvider, globalOptions, sp, source);
        }

        var sourceOptions = new CouchbaseOptions
        {
            ConnectionString = connectionString,
            Bucket = bucket,
            Username = username,
            Password = password,
            ManagementUrl = managementUrl,
            Scope = baseOptions.Scope,
            Collection = baseOptions.Collection,
            CollectionName = baseOptions.CollectionName,
            NamingStyle = baseOptions.NamingStyle,
            Separator = baseOptions.Separator,
            DefaultPageSize = baseOptions.DefaultPageSize,
            QueryTimeout = baseOptions.QueryTimeout,
            DurabilityLevel = baseOptions.DurabilityLevel,
            Readiness = baseOptions.Readiness,
        };
        var optionsMonitor = new CouchbaseStaticOptionsMonitor<CouchbaseOptions>(sourceOptions);

        // One provider per distinct physical placement — keyed by the FULL cluster identity (the same five fields the
        // Default-dedup compares), not just connection+bucket: two sources with the same connection+bucket but DIFFERENT
        // credentials are distinct clusters and must NOT share a provider (a narrower key would authenticate one as the
        // other — a cross-source credential leak). A value-tuple key compares the parts ordinally with no delimiter risk.
        var provider = _sourceProviders.GetOrAdd(
            (connectionString, bucket, username, password, managementUrl),
            _ => new CouchbaseClusterProvider(optionsMonitor, sp.GetService<ILogger<CouchbaseClusterProvider>>()));

        return new CouchbaseDocumentStore<TEntity, TKey>(provider, optionsMonitor, sp, source);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _sourceProviders.Values)
            await provider.DisposeAsync().ConfigureAwait(false);
        _sourceProviders.Clear();
    }

    // The factory is a DI singleton; the host disposes it async on teardown. Implement IDisposable too so a SYNC
    // ServiceProvider.Dispose() doesn't throw on this IAsyncDisposable-only singleton.
    public void Dispose()
    {
        foreach (var provider in _sourceProviders.Values)
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _sourceProviders.Clear();
    }

    // Couchbase isolates a partition through a native scope (bucket.scope.collection), not a name suffix —
    // CouchbaseClusterProvider.GetCollectionContext routes EntityContext.Current.Partition into the scope
    // position via FormatScope. So the capability announces EncodePartitionInName = false: the framework
    // generates just the collection name, and a fixed Collection / CollectionName callback overrides it.
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            // Couchbase collection names allow only [A-Za-z0-9_-%] — a '.' separator (the FullNamespace
            // default) produces an invalid collection name and CreateCollectionAsync fails. Use '_'.
            Separator = "_",
            Casing = NameCasing.AsIs,
            // Collection names cap at 251 bytes; announce it so the framework hashes overlong names down rather
            // than handing Couchbase an invalid identifier (partition rides the scope, so this bounds the name).
            MaxIdentifierBytes = 251,
            EncodePartitionInName = false,
            NameOverride = entityType => !string.IsNullOrWhiteSpace(options.Collection)
                ? options.Collection!.Trim()
                : options.CollectionName?.Invoke(entityType),
        };
    }

    /// <summary>
    /// Format a partition value as a Couchbase scope identifier (alphanumeric / underscore / hyphen /
    /// percent, max 30 bytes). Used by CouchbaseClusterProvider when mapping EntityContext.Current.Partition
    /// onto bucket.scope.collection.
    /// </summary>
    public static string FormatScope(string partition) => FormatIdentifier(partition, 30);

    /// <summary>
    /// Format an entity collection name to Couchbase's collection-identifier charset (alphanumeric / underscore /
    /// hyphen / percent, max 251 bytes). The framework's <c>StorageNameResolver</c> replaces only <c>'.'</c> with the
    /// separator, so a NESTED-type entity (e.g. <c>ManagedFieldNoLeak+ScopedDoc</c>) keeps a <c>'+'</c> the Couchbase
    /// collection manager rejects. This closes that gap the same way <see cref="FormatScope"/> closes it for scopes —
    /// applied once where the name is resolved (<see cref="CouchbaseDocumentStore{TEntity,TKey}"/>) so create and query
    /// agree. A name already in the charset passes through unchanged (the common top-level-entity case).
    /// </summary>
    public static string FormatCollectionName(string name) => FormatIdentifier(name, 251);

    // The shared Couchbase identifier rule (scope + collection): keep [A-Za-z0-9_-%]; replace any other char with '_';
    // when the sanitized form is FAITHFUL (no char replaced) AND fits the byte budget, return it as-is — otherwise
    // append a deterministic hash of the ORIGINAL so two distinct inputs can never collapse onto one identifier (both
    // lossy '_' replacement and over-length truncation are collision sources; hashing the original closes both).
    private static string FormatIdentifier(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var sb = new System.Text.StringBuilder(value.Length);
        var faithful = true;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '%') sb.Append(c);
            else { sb.Append('_'); faithful = false; }
        }
        var sanitized = sb.ToString();
        if (faithful && NamingUtils.ByteLength(sanitized) <= maxBytes) return sanitized;
        var hash = NamingUtils.ShortHash(value, 8);
        return NamingUtils.TrimToBytes(sanitized, maxBytes - hash.Length - 1) + "_" + hash;
    }
}
