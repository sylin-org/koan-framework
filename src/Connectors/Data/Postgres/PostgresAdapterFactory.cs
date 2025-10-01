using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.Postgres;

[ProviderPriority(14)]
[KoanService(ServiceKind.Database, shortCode: "postgres", name: "PostgreSQL",
    ContainerImage = "postgres",
    DefaultTag = "16",
    DefaultPorts = new[] { 5432 },
    Capabilities = new[] { "protocol=postgres" },
    Env = new[] { "POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=Koan" },
    Volumes = new[] { "./Data/postgres:/var/lib/postgresql/data" },
    AppEnv = new[] { "Koan__Data__Postgres__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Postgres__Database=Koan" },
    Scheme = "postgres", Host = "postgres", EndpointPort = 5432, UriPattern = "postgres://{host}:{port}",
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 5432, LocalPattern = "postgres://{host}:{port}")]
public sealed class PostgresAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider)
        => string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "npgsql", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOpts = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
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
                "Postgres",
                source);
        }

        // Create source-specific options
        var sourceOpts = new PostgresOptions
        {
            ConnectionString = connectionString,
            DefaultPageSize = baseOpts.DefaultPageSize,
            MaxPageSize = baseOpts.MaxPageSize,
            DdlPolicy = baseOpts.DdlPolicy,
            SchemaMatching = baseOpts.SchemaMatching,
            AllowProductionDdl = baseOpts.AllowProductionDdl,
            SearchPath = baseOpts.SearchPath,
            NamingStyle = baseOpts.NamingStyle,
            Separator = baseOpts.Separator,
            Readiness = baseOpts.Readiness
        };

        return new PostgresRepository<TEntity, TKey>(sp, sourceOpts, resolver);
    }
}

