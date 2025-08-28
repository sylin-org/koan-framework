using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

namespace Sora.Data.SqlServer;

[ProviderPriority(15)]
[SoraService(ServiceKind.Database, shortCode: "mssql", name: "SQL Server",
    ContainerImage = "mcr.microsoft.com/mssql/server",
    DefaultTag = "2022-latest",
    DefaultPorts = new[] { 1433 },
    Capabilities = new[] { "protocol=mssql" },
    Env = new[] { "ACCEPT_EULA=Y", "SA_PASSWORD" },
    Volumes = new[] { "./Data/mssql:/var/opt/mssql" },
    AppEnv = new[] { "Sora__Data__SqlServer__ConnectionString={scheme}://{host}:{port}" },
    Scheme = "mssql", Host = "mssql", EndpointPort = 1433, UriPattern = "mssql://{host}:{port}",
    LocalScheme = "mssql", LocalHost = "localhost", LocalPort = 1433, LocalPattern = "mssql://{host}:{port}")]
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
