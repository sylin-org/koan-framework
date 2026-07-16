using System;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
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
    public string Provider => "sqlite";

    internal static bool HandlesProvider(string provider)
        => string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase);

    public bool CanHandle(string provider) => HandlesProvider(provider);

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
        var connections = sp.GetRequiredService<SqliteConnectionLifecycle>();

        // Resolve the source's connection through the shared resolver: the Default (or a non-Default whose source
        // relies on discovery and resolves to "auto") collapses onto the discovery-resolved base connection, so a
        // routed source never keys its store on the unresolved sentinel (ARCH-0103 P5 fleet hoist).
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            config, sourceRegistry, "Sqlite", source, baseOpts.ConnectionString, CanHandle);

        // Create source-specific options
        var sourceOpts = new SqliteOptions
        {
            ConnectionString = connectionString,
            NamingStyle = baseOpts.NamingStyle,
            Separator = baseOpts.Separator,
            DefaultPageSize = baseOpts.DefaultPageSize,
            DdlPolicy = baseOpts.DdlPolicy,
            SchemaMatching = baseOpts.SchemaMatching,
            AllowProductionDdl = baseOpts.AllowProductionDdl,
            Readiness = baseOpts.Readiness
        };

        return new SqliteRepository<TEntity, TKey>(sp, sourceOpts, resolver, connections, source);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqliteOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = opts.NamingStyle,
            Separator = opts.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            // SQLite keeps letters/digits and - . _ ; no practical identifier-length limit.
            Partition = PartitionTokenPolicy.Default,
        };
    }
}
