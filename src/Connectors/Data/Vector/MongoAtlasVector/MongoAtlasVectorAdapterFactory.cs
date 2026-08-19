using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

/// <summary>Creates immutable-plan-bound repositories over MongoDB Atlas Vector Search.</summary>
[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
public sealed class MongoAtlasVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<MongoAtlasVectorOptions> options) : IVectorAdapterFactory
{
    private readonly MongoAtlasVectorOptions _options = Validate(options.Value);

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
        Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = false },
        MaxIdentifierBytes = 120
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
                "MongoAtlasVector exposes awaited Atlas Search mutations with Session visibility and does not simulate Eventual visibility.");
        return new MongoAtlasVectorRepository<TEntity, TKey>(
            services,
            this,
            plan,
            ResolveRoute(plan.Source),
            services.GetRequiredService<MongoAtlasVectorClientManager>(),
            _options);
    }

    internal MongoAtlasVectorRoute ResolveRoute(string source) =>
        MongoAtlasVectorRoute.Resolve(configuration, sources, _options, this, source);

    private static MongoAtlasVectorOptions Validate(MongoAtlasVectorOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.Database)) throw Invalid(nameof(value.Database));
        if (value.CommandTimeoutSeconds <= 0) throw Invalid(nameof(value.CommandTimeoutSeconds));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxSearchCandidates <= 0) throw Invalid(nameof(value.MaxSearchCandidates));
        if (value.IndexReadyTimeoutSeconds <= 0) throw Invalid(nameof(value.IndexReadyTimeoutSeconds));
        if (value.MutationVisibilityTimeoutSeconds <= 0) throw Invalid(nameof(value.MutationVisibilityTimeoutSeconds));
        if (value.VisibilityPollMilliseconds <= 0) throw Invalid(nameof(value.VisibilityPollMilliseconds));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"MongoAtlasVectorOptions.{name} must be nonblank or greater than zero.");
}
