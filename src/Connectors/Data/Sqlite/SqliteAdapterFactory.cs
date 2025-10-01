using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.Sqlite;

[ProviderPriority(10)]
[KoanService(ServiceKind.Database, shortCode: "sqlite", name: "SQLite",
    DeploymentKind = DeploymentKind.InProcess,
    DefaultPorts = new int[] { }, // SQLite is file-based, no network ports
    Capabilities = new[] { "protocol=file" },
    Volumes = new[] { "./Data/sqlite:/data" },
    AppEnv = new[] { "Koan__Data__Sqlite__ConnectionString=Data Source=/data/app.db" },
    Scheme = "file", Host = "", EndpointPort = 0,
    UriPattern = "Data Source={path}", LocalScheme = "file", LocalHost = "", LocalPort = 0, LocalPattern = "Data Source={path}")]
public sealed class SqliteAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOpts = sp.GetRequiredService<IOptions<SqliteOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();

        // Resolve source-specific connection string
        // If base options already have a connection (from discovery), use it for Default source
        string connectionString;
        if (source == "Default" && !string.IsNullOrWhiteSpace(baseOpts.ConnectionString))
        {
            connectionString = baseOpts.ConnectionString;
        }
        else
        {
            connectionString = AdapterConnectionResolver.ResolveConnectionString(
                config,
                sourceRegistry,
                "Sqlite",
                source);
        }

        // Create source-specific options
        var sourceOpts = new SqliteOptions
        {
            ConnectionString = connectionString,
            NamingStyle = baseOpts.NamingStyle,
            Separator = baseOpts.Separator,
            DefaultPageSize = baseOpts.DefaultPageSize,
            MaxPageSize = baseOpts.MaxPageSize,
            DdlPolicy = baseOpts.DdlPolicy,
            SchemaMatching = baseOpts.SchemaMatching,
            AllowProductionDdl = baseOpts.AllowProductionDdl,
            Readiness = baseOpts.Readiness
        };

        return new SqliteRepository<TEntity, TKey>(sp, sourceOpts, resolver);
    }
}
