using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Couchbase.Infrastructure;
using Koan.Data.Connector.Couchbase.Runtime;
using Koan.Data.Core;
using Couchbase.KeyValue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Couchbase;

[ProviderPriority(Constants.Priority)]
[KoanService(ServiceKind.Database, shortCode: Constants.Provider, name: "Couchbase",
    ContainerImage = "couchbase/server", DefaultTag = "8.0.2",
    DefaultPorts = [8091, 8092, 8093, 8094, 11210], Capabilities = ["protocol=couchbase"],
    Volumes = ["./Data/couchbase-8.0:/opt/couchbase/var"],
    AppEnv = ["Koan__Data__Couchbase__ConnectionString=couchbase://{host}", "Koan__Data__Couchbase__Bucket=Koan"],
    Scheme = "couchbase", Host = "couchbase", EndpointPort = 8091, UriPattern = "couchbase://{host}",
    LocalScheme = "couchbase", LocalHost = "localhost", LocalPort = 8091, LocalPattern = "couchbase://{host}")]
public sealed class CouchbaseAdapterFactory : IDataAdapterFactory, IDataSourceIntegrationFactory
{
    public string Provider => Constants.Provider;
    public IReadOnlyCollection<string> Aliases => [Constants.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Couchbase"];

    public void DescribeClaims(IDataClaims claims) => CouchbaseFeatures.Declare(claims);

    public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
        SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress |
        SourceInspectionCapabilities.DescribeContainer | SourceInspectionCapabilities.SampleRecords,
        ["sql"],
        enforcesReadLanes: true);

    public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
    {
        var route = ResolveRoute(services, source);
        return new CouchbaseSourceIntegration(route, services.GetRequiredService<CouchbaseResourcePool>());
    }

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Constants.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var route = ResolveRoute(services, source);
        var mapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(route.Source);
        return new CouchbaseRepository<TEntity, TKey>(
            services,
            this,
            route,
            services.GetRequiredService<CouchbaseResourcePool>(),
            mapping);
    }

    internal CouchbaseRoute ResolveRoute(IServiceProvider services, string source)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var defaults = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? Constants.DefaultSource : source;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration, registry, Provider, resolvedSource, defaults.ConnectionString, this);
        var bucket = Setting(configuration, registry, resolvedSource, "Bucket", defaults.Bucket);
        var scope = Setting(configuration, registry, resolvedSource, "Scope", defaults.Scope);
        var username = EmptyAsNull(Setting(configuration, registry, resolvedSource, "Username", defaults.Username ?? ""));
        var password = EmptyAsNull(Setting(configuration, registry, resolvedSource, "Password", defaults.Password ?? ""));
        var definition = registry.GetSource(resolvedSource);
        var lanes = definition?.ReadLanes?
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.Value.ConnectionString))
            .ToDictionary(static lane => lane.Key, static lane => lane.Value.ConnectionString, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new CouchbaseRoute(
            resolvedSource,
            connection,
            bucket,
            scope,
            defaults.Collection,
            username,
            password,
            defaults.QueryTimeout,
            defaults.BootstrapTimeout,
            defaults.BootstrapPollInterval,
            ResolveDurability(defaults.Durability),
            definition?.StorageLifecycle ?? StorageLifecycle.Managed,
            definition?.Access ?? DataSourceAccess.ReadWrite,
            registry.GetPlan(resolvedSource, Provider, connection),
            lanes);
    }

    private string Setting(
        IConfiguration configuration,
        DataSourceRegistry registry,
        string source,
        string key,
        string fallback) => AdapterConnectionResolver.GetSourceSetting(
            configuration, registry, Provider, source, key, fallback, this);

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = options.NamingStyle,
            Separator = options.Separator,
            Casing = NameCasing.AsIs,
            EncodePartitionInName = false,
            NameOverride = entity => !string.IsNullOrWhiteSpace(options.Collection)
                ? options.Collection.Trim()
                : options.CollectionName?.Invoke(entity),
            MaxIdentifierBytes = Constants.MaximumCollectionBytes
        };
    }

    internal static string FormatScope(string value) => FormatIdentifier(value, Constants.MaximumScopeBytes);
    internal static string FormatCollection(string value) => FormatIdentifier(value, Constants.MaximumCollectionBytes);

    private static string FormatIdentifier(string value, int maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var builder = new System.Text.StringBuilder(value.Length);
        var faithful = true;
        foreach (var character in value)
            if (char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '%') builder.Append(character);
            else { builder.Append('_'); faithful = false; }
        var sanitized = builder.ToString();
        if (faithful && NamingUtils.ByteLength(sanitized) <= maximumBytes) return sanitized;
        var hash = NamingUtils.ShortHash(value, 8);
        return NamingUtils.TrimToBytes(sanitized, maximumBytes - hash.Length - 1) + "_" + hash;
    }

    private static string? EmptyAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DurabilityLevel ResolveDurability(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DurabilityLevel.None;
        if (Enum.TryParse<DurabilityLevel>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new InvalidOperationException(
            $"Couchbase durability '{value}' is invalid. Use None, Majority, MajorityAndPersistToActive, or PersistToMajority.");
    }
}

internal sealed record CouchbaseRoute(
    string Source,
    string ConnectionString,
    string Bucket,
    string DefaultScope,
    string? FixedCollection,
    string? Username,
    string? Password,
    TimeSpan QueryTimeout,
    TimeSpan BootstrapTimeout,
    TimeSpan BootstrapPollInterval,
    DurabilityLevel Durability,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access,
    DataSourcePlan Plan,
    IReadOnlyDictionary<string, string> ReadLanes);
