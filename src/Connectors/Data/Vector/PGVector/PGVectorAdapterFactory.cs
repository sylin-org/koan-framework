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

    // INamingProvider implementation
    public string RepositorySeparator => "#";

    public string GetStorageName(Type entityType, IServiceProvider services)
    {
        // Use lowercase with underscores (PostgreSQL convention)
        var convention = new StorageNameResolver.Convention(
            NamingStyle.TypeName,
            separator: "_",
            casing: NameCasing.Lower);

        return StorageNameResolver.Resolve(entityType, convention);
    }

    public string GetConcretePartition(string partition)
    {
        // Postgres: Remove hyphens from GUIDs, lowercase, sanitize
        if (Guid.TryParse(partition, out var guid))
            return guid.ToString("N");  // N format = no hyphens, lowercase

        // Named partitions: lowercase and sanitize for PostgreSQL table names
        return partition.ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_")
            .Replace(".", "_");
    }
}
