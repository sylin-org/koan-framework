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
    public string Provider => "mssql";

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
        // If base options already have a connection (from discovery) and no specific source requested, use it
        string connectionString;
        if ((string.IsNullOrWhiteSpace(source) || string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(baseOpts.ConnectionString))
        {
            connectionString = baseOpts.ConnectionString;
        }
        else
        {
            connectionString = AdapterConnectionResolver.ResolveConnectionString(
                config,
                sourceRegistry,
                "SqlServer",
                source);
        }

        // Create source-specific options
        var sourceOpts = new SqlServerOptions
        {
            ConnectionString = connectionString,
            DefaultPageSize = baseOpts.DefaultPageSize,
            MaxPageSize = baseOpts.MaxPageSize,
            JsonCaseInsensitive = baseOpts.JsonCaseInsensitive,
            JsonWriteIndented = baseOpts.JsonWriteIndented,
            JsonIgnoreNullValues = baseOpts.JsonIgnoreNullValues,
            DdlPolicy = baseOpts.DdlPolicy,
            SchemaMatching = baseOpts.SchemaMatching,
            AllowProductionDdl = baseOpts.AllowProductionDdl,
            NamingStyle = baseOpts.NamingStyle,
            Separator = baseOpts.Separator,
            Readiness = baseOpts.Readiness
        };

        return new SqlServerRepository<TEntity, TKey>(sp, sourceOpts, resolver);
    }

    // INamingProvider implementation
    public string RepositorySeparator => "#";

    public string GetStorageName(Type entityType, IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        var convention = new StorageNameResolver.Convention(
            opts.NamingStyle,
            opts.Separator,
            NameCasing.AsIs);

        return StorageNameResolver.Resolve(entityType, convention);
    }

    public string GetConcretePartition(string partition)
    {
        // SQL Server: Remove hyphens from GUIDs, lowercase
        if (Guid.TryParse(partition, out var guid))
            return guid.ToString("N");  // N format = no hyphens, lowercase

        // Named partitions: lowercase for SQL Server convention
        return partition.ToLowerInvariant();
    }
}

