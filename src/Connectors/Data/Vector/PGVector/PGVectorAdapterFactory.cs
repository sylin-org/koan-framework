using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// Factory for creating PGVector repository instances.
/// Implements IVectorAdapterFactory for Koan's provider-agnostic vector infrastructure.
/// Handles connection pooling, naming conventions, and PostgreSQL-specific optimizations.
/// </summary>
[ProviderPriority(15)] // Higher than generic Postgres (14), used when [VectorAdapter("pgvector")] specified
[KoanService(ServiceKind.Database, shortCode: "pgvector", name: "PostgreSQL + pgvector",
    ContainerImage = "pgvector/pgvector",
    DefaultTag = "pg16",
    DefaultPorts = new[] { 5432 },
    Capabilities = new[] { "protocol=postgres", "feature=vector", "feature=semantic-search" },
    Env = new[] { "POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=Koan" },
    Volumes = new[] { "./Data/pgvector:/var/lib/postgresql/data" },
    AppEnv = new[] { "Koan__Vector__PGVector__ConnectionString={scheme}://{host}:{port}", "Koan__Vector__PGVector__Database=Koan" },
    Scheme = "postgres", Host = "pgvector", EndpointPort = 5432, UriPattern = "postgres://{host}:{port}",
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 5432, LocalPattern = "postgres://{host}:{port}")]
public sealed class PGVectorAdapterFactory : IVectorAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    static PGVectorAdapterFactory()
    {
        // Teach Dapper to bind Pgvector.Vector parameters (UseVector() only covers the Npgsql side).
        // Without this every embedding upsert/search throws "cannot be used as a parameter value".
        Dapper.SqlMapper.AddTypeHandler(new Pgvector.Dapper.VectorTypeHandler());
    }

    public PGVectorAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public string Provider => "pgvector";

    public bool CanHandle(string provider)
        => string.Equals(provider, "pgvector", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "pg-vector", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "postgres-vector", StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var options = sp.GetRequiredService<IOptions<PGVectorOptions>>();
        var extensionManager = sp.GetRequiredService<PgVectorExtensionManager>();
        var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PGVectorRepository<TEntity, TKey>>>();

        // Create NpgsqlDataSource for connection pooling
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.Value.ConnectionString);

        // Register Pgvector types
        dataSourceBuilder.UseVector();

        var dataSource = dataSourceBuilder.Build();

        return new PGVectorRepository<TEntity, TKey>(dataSource, sp, options, extensionManager, logger);
    }

    // PGVector is Postgres-backed: lowercased EntityType names, '#' partition separator, and the same
    // 63-byte identifier limit (the framework hashes the composed name on overflow). Partition tokens keep
    // only [A-Za-z0-9_] (hyphens/dots/spaces fold to '_').
    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Separator = "_",
            Casing = NameCasing.Lower,
            PartitionSeparator = '#',
            Partition = new PartitionTokenPolicy { GuidFormat = "N", Lowercase = true, AllowedExtraChars = "" },
            MaxIdentifierBytes = 63,
        };
}
