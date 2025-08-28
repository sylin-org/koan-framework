using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

namespace Sora.Data.Postgres;

[ProviderPriority(14)]
[SoraService(ServiceKind.Database, shortCode: "postgres", name: "PostgreSQL",
    ContainerImage = "postgres",
    DefaultTag = "16",
    DefaultPorts = new[] { 5432 },
    Capabilities = new[] { "protocol=postgres" },
    Env = new[] { "POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=sora" },
    Volumes = new[] { "./Data/postgres:/var/lib/postgresql/data" },
    AppEnv = new[] { "Sora__Data__Postgres__ConnectionString={scheme}://{host}:{port}", "Sora__Data__Postgres__Database=sora" },
    Scheme = "postgres", Host = "postgres", EndpointPort = 5432, UriPattern = "postgres://{host}:{port}",
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 5432, LocalPattern = "postgres://{host}:{port}")]
public sealed class PostgresAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "npgsql", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new PostgresRepository<TEntity, TKey>(sp, opts, resolver);
    }
}
