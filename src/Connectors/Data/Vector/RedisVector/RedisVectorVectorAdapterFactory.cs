using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Koan.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.RedisVector;

/// <summary>Creates immutable-plan-bound Redis Search vector repositories over Koan's shared Redis connection.</summary>
[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
public sealed class RedisVectorVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IRedisConnectionProvider connections,
    IOptions<RedisVectorOptions> options) : IVectorAdapterFactory
{
    private readonly RedisVectorOptions _options = Validate(options.Value);

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Separator = "_",
        Casing = NameCasing.AsIs,
        NameOverride = entity => StorageNameResolver.Resolve(
            entity,
            new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.AsIs)) + "_vector",
        PartitionSeparator = '#',
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = false }
    };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        VectorSpacePlan plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Visibility != VectorVisibility.Session)
            throw new NotSupportedException(
                "RedisVector exposes awaited Redis Search mutations with Session visibility and does not simulate Eventual visibility.");
        return new RedisVectorRepository<TEntity, TKey>(
            services,
            this,
            plan,
            ResolveRoute(plan.Source),
            _options);
    }

    internal RedisVectorRoute ResolveRoute(string source) =>
        RedisVectorRoute.Resolve(configuration, sources, connections, this, source);

    private static RedisVectorOptions Validate(RedisVectorOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxSearchCandidates <= 0) throw Invalid(nameof(value.MaxSearchCandidates));
        if (value.MaxIndexedPaths <= 0) throw Invalid(nameof(value.MaxIndexedPaths));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"RedisVectorOptions.{name} must be greater than zero.");
}
