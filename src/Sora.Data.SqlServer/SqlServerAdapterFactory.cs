using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration.Attributes;

namespace Sora.Data.SqlServer;

[ProviderPriority(15)]
[ServiceId("mssql")]
[ContainerDefaults(
    image: "mcr.microsoft.com/mssql/server",
    Tag = "2022-latest",
    Ports = new[] { 1433 },
    Volumes = new[] { "./Data/mssql:/var/opt/mssql" },
    Env = new[] { "ACCEPT_EULA=Y", "SA_PASSWORD" }
)]
[EndpointDefaults(EndpointMode.Container, scheme: "mssql", host: "mssql", port: 1433, UriPattern = "mssql://{host}:{port}")]
[EndpointDefaults(EndpointMode.Local, scheme: "mssql", host: "localhost", port: 1433, UriPattern = "mssql://{host}:{port}")]
[AppEnvDefaults(
    "Sora__Data__SqlServer__ConnectionString={scheme}://{host}:{port}"
)]
public sealed class SqlServerAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "mssql", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();
        return new SqlServerRepository<TEntity, TKey>(sp, opts, resolver);
    }
}
