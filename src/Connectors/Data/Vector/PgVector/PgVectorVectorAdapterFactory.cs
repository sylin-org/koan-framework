using Koan.Core;
using Koan.Core.Services;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.PgVector;

/// <summary>Creates immutable-plan-bound pgvector repositories.</summary>
[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
[KoanService(ServiceKind.Vector, shortCode: Infrastructure.Constants.Provider.Name, name: "PostgreSQL + pgvector",
    ContainerImage = "pgvector/pgvector", DefaultTag = "pg16", DefaultPorts = [5432],
    Capabilities = ["protocol=postgres", "vector-search=true", "filters=true", "session-visibility=true"],
    Env = ["POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=Koan"],
    Volumes = ["./Data/pgvector-pg16:/var/lib/postgresql/data"],
    AppEnv = ["Koan__Data__PgVector__ConnectionString={scheme}://{host}:{port}"],
    Scheme = "postgres", Host = Infrastructure.Constants.Provider.Name, EndpointPort = 5432,
    UriPattern = "postgres://{host}:{port}", LocalScheme = "postgres", LocalHost = "localhost",
    LocalPort = 5432, LocalPattern = "postgres://{host}:{port}")]
public sealed class PgVectorVectorAdapterFactory(
    IConfiguration configuration,
    DataSourceRegistry sources,
    IOptions<PgVectorOptions> options) : IVectorAdapterFactory
{
    private readonly PgVectorOptions _options = Validate(options.Value);

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
        MaxIdentifierBytes = 63
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
                "PgVector commits awaited PostgreSQL mutations with Session visibility and does not simulate Eventual visibility.");
        return new PgVectorRepository<TEntity, TKey>(
            services,
            this,
            plan,
            ResolveRoute(plan.Source),
            _options);
    }

    internal PgVectorRoute ResolveRoute(string source) =>
        PgVectorRoute.Resolve(configuration, sources, _options, this, source);

    private static PgVectorOptions Validate(PgVectorOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.CommandTimeoutSeconds <= 0) throw Invalid(nameof(value.CommandTimeoutSeconds));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        if (value.MaxBatchPoints <= 0) throw Invalid(nameof(value.MaxBatchPoints));
        if (value.MaxSearchCandidates <= 0) throw Invalid(nameof(value.MaxSearchCandidates));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"PgVectorOptions.{name} must be greater than zero.");
}
