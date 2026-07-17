using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
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
    public IReadOnlyCollection<string> Aliases => ["sqlserver", "microsoft.sqlserver"];

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

        // Resolve the source's connection through the shared resolver: the Default (or a non-Default whose source
        // relies on discovery and resolves to "auto") collapses onto the discovery-resolved base connection, so a
        // routed source never keys its store on the unresolved sentinel (ARCH-0103 P5 fleet hoist).
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            config, sourceRegistry, "SqlServer", source, baseOpts.ConnectionString, this);

        // Create source-specific options
        var sourceOpts = new SqlServerOptions
        {
            ConnectionString = connectionString,
            DefaultPageSize = baseOpts.DefaultPageSize,
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

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = opts.NamingStyle,
            Separator = opts.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = 128, // SQL Server sysname limit
        };
    }
}

