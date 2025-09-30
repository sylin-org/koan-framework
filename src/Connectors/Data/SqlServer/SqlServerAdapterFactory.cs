using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.SqlServer;

[ProviderPriority(15)]
[KoanService(ServiceKind.Database, shortCode: "mssql", name: "SQL Server",
    ContainerImage = "mcr.microsoft.com/mssql/server",
    DefaultTag = "2022-latest",
    DefaultPorts = new[] { 1433 },
    Capabilities = new[] { "protocol=mssql" },
    Env = new[] { "ACCEPT_EULA=Y", "SA_PASSWORD" },
    Volumes = new[] { "./Data/mssql:/var/opt/mssql" },
    AppEnv = new[] { "Koan__Data__SqlServer__ConnectionString={scheme}://{host}:{port}" },
    Scheme = "mssql", Host = "mssql", EndpointPort = 1433, UriPattern = "mssql://{host}:{port}",
    LocalScheme = "mssql", LocalHost = "localhost", LocalPort = 1433, LocalPattern = "mssql://{host}:{port}")]
public sealed class SqlServerAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "mssql", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOpts = sp.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();

        // Resolve source-specific connection string
        var connectionString = AdapterConnectionResolver.ResolveConnectionString(
            config,
            sourceRegistry,
            "SqlServer",
            source);

        // Create source-specific options
        var sourceOpts = new SqlServerOptions
        {
            ConnectionString = connectionString,
            DefaultPageSize = baseOpts.DefaultPageSize,
            MaxPageSize = baseOpts.MaxPageSize,
            CommandTimeoutSeconds = baseOpts.CommandTimeoutSeconds,
            MaxRetryCount = baseOpts.MaxRetryCount,
            MaxRetryDelaySeconds = baseOpts.MaxRetryDelaySeconds,
            DdlPolicy = baseOpts.DdlPolicy,
            SchemaMatchingMode = baseOpts.SchemaMatchingMode,
            AllowProductionDdl = baseOpts.AllowProductionDdl,
            Readiness = baseOpts.Readiness
        };

        return new SqlServerRepository<TEntity, TKey>(sp, sourceOpts, resolver);
    }
}

