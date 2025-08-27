using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration;
using Sora.Orchestration.Abstractions.Attributes;

namespace Sora.Data.Postgres;

[ProviderPriority(14)]
[ServiceId("postgres")]
[ContainerDefaults(
    image: "postgres",
    Tag = "16",
    Ports = new[] { 5432 },
    Volumes = new[] { "./Data/postgres:/var/lib/postgresql/data" },
    Env = new[] { "POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=sora" }
)]
[EndpointDefaults(EndpointMode.Container, scheme: "postgres", host: "postgres", port: 5432, UriPattern = "postgres://{host}:{port}")]
[EndpointDefaults(EndpointMode.Local, scheme: "postgres", host: "localhost", port: 5432, UriPattern = "postgres://{host}:{port}")]
[AppEnvDefaults(
    "Sora__Data__Postgres__ConnectionString={scheme}://{host}:{port}",
    "Sora__Data__Postgres__Database=sora"
)]
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