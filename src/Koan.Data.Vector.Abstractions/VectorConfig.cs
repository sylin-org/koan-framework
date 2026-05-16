using System;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Vector-specific configuration for entity.
/// Parallel to AggregateConfig but for vector operations.
/// Immutable value holder for provider and service provider.
/// </summary>
public sealed class VectorConfig<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Vector provider identifier (e.g., "weaviate", "milvus", "qdrant")
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Service provider for dependency resolution
    /// </summary>
    public IServiceProvider Services { get; }

    internal VectorConfig(string provider, IServiceProvider services)
    {
        Provider = provider;
        Services = services;
    }
}
