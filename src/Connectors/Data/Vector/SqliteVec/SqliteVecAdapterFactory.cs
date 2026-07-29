using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>Creates plan-bound repositories for the embedded stable sqlite-vec provider.</summary>
[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
public sealed class SqliteVecAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<SqliteVecOptions> options) : IVectorAdapterFactory
{
    private readonly SqliteVecOptions _options = Validate(options.Value);

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = '#',
        Partition = PartitionTokenPolicy.Default
    };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        VectorSpacePlan plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Metric == VectorMetric.DotProduct)
            throw new NotSupportedException(
                "SqliteVec stable vec0 supports Cosine and Euclidean spaces, not DotProduct. Choose a supported metric or another adapter.");
        if (plan.Visibility != VectorVisibility.Session)
            throw new NotSupportedException(
                "SqliteVec commits awaited mutations with Session visibility and does not simulate Eventual visibility.");
        return new SqliteVecRepository<TEntity, TKey>(
            services,
            this,
            plan,
            ResolveRoute(plan.Source),
            _options,
            services.GetRequiredService<SqliteVecNative>());
    }

    internal SqliteVecRoute ResolveRoute(string source) =>
        SqliteVecRoute.Resolve(configuration, sources, _options, this, source);

    private static SqliteVecOptions Validate(SqliteVecOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.MaxMetadataBytesPerPoint <= 0)
            throw new InvalidOperationException("SqliteVecOptions.MaxMetadataBytesPerPoint must be greater than zero.");
        if (value.MaxSearchCandidates <= 0)
            throw new InvalidOperationException("SqliteVecOptions.MaxSearchCandidates must be greater than zero.");
        return value;
    }
}
