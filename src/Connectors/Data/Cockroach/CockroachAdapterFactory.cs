using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Core.Services;
using Koan.Data.Relational.Npgsql;

namespace Koan.Data.Connector.Cockroach;

// CockroachDB speaks the PostgreSQL wire protocol (Npgsql) + nearly the same SQL dialect, so this adapter REUSES
// the Postgres connector's repository/dialect/DDL (ARCH-0094 §2.4). Priority 13 is distinct from Postgres (14);
// Its identities are only cockroach/cockroachdb (the `npgsql` alias stays with the Postgres factory, so an app that
// references both connectors resolves each engine unambiguously). pg-wire default port is 26257 on Cockroach.
[ProviderPriority(13)]
[KoanService(ServiceKind.Database, shortCode: "cockroach", name: "CockroachDB",
    ContainerImage = "cockroachdb/cockroach",
    DefaultTag = "v26.2.3",
    DefaultPorts = new[] { 26257 },
    Capabilities = new[] { "protocol=postgres" },
    Volumes = new[] { "./Data/cockroach-26.2:/cockroach/cockroach-data" },
    AppEnv = new[] { "Koan__Data__Cockroach__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Cockroach__Database=Koan" },
    Scheme = "postgres", Host = "cockroach", EndpointPort = 26257, UriPattern = "postgres://{host}:{port}",
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 26257, LocalPattern = "postgres://{host}:{port}")]
public sealed class CockroachAdapterFactory : IDataAdapterFactory
{
    public string Provider => "cockroach";
    public IReadOnlyCollection<string> Aliases => ["cockroachdb"];

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOpts = sp.GetRequiredService<IOptions<CockroachOptions>>().Value;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();

        // Resolve the source's connection through the shared resolver: the Default (or a non-Default whose source
        // relies on discovery and resolves to "auto") collapses onto the discovery-resolved base connection, so a
        // routed source never keys its store on the unresolved sentinel (ARCH-0103 P5 fleet hoist).
        var connectionString = AdapterConnectionResolver.ResolveRoutedConnection(
            config, sourceRegistry, "Cockroach", source, baseOpts.ConnectionString, this);

        // Create source-specific options
        var sourceOpts = new CockroachOptions
        {
            ConnectionString = connectionString,
            DdlPolicy = baseOpts.DdlPolicy,
            SchemaMatching = baseOpts.SchemaMatching,
            AllowProductionDdl = baseOpts.AllowProductionDdl,
            SearchPath = baseOpts.SearchPath,
            NamingStyle = baseOpts.NamingStyle,
            Separator = baseOpts.Separator,
            Readiness = baseOpts.Readiness
        };

        return new NpgsqlRepository<TEntity, TKey>(sp, new NpgsqlRepositoryOptions
        {
            ProviderName = Provider,
            ConnectionString = sourceOpts.ConnectionString,
            DdlPolicy = sourceOpts.DdlPolicy,
            SchemaMatching = sourceOpts.SchemaMatching,
            AllowProductionDdl = sourceOpts.AllowProductionDdl,
            SearchPath = sourceOpts.SearchPath,
            NamingStyle = sourceOpts.NamingStyle,
            Separator = sourceOpts.Separator,
            StableOrderClause = "ORDER BY \"Id\""
        }, resolver);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<CockroachOptions>>().Value;
        return new StorageNamingCapability
        {
            Style = opts.NamingStyle,
            Separator = opts.Separator,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            // Named partitions lowercased; GUIDs as 32-hex. CockroachDB truncates identifiers at 63 bytes,
            // so the framework hashes the composed name when it would overflow (preserving isolation).
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true },
            MaxIdentifierBytes = 63,
        };
    }
}
