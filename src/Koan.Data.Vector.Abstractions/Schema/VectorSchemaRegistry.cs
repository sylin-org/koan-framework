using System;
using System.Collections.Concurrent;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions.Schema;

public sealed class VectorSchemaRegistry
{
    private readonly ConcurrentDictionary<(Type Entity, Type Key), VectorSchemaDescriptor> _cache = new();

    public VectorSchemaDescriptor? TryGet<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var descriptor = GetOrCreateDescriptor(typeof(TEntity), typeof(TKey));
        return descriptor.IsFallback ? null : descriptor;
    }

    public VectorSchemaDescriptor Get<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        return GetOrCreateDescriptor(typeof(TEntity), typeof(TKey));
    }

    private VectorSchemaDescriptor GetOrCreateDescriptor(Type entityType, Type keyType)
    {
        return _cache.GetOrAdd((entityType, keyType), static entry =>
        {
            var descriptor = VectorSchemaAutoGenerator.TryCreate(entry.Entity);
            return descriptor ?? VectorSchemaDescriptor.CreateFallback(entry.Entity);
        });
    }
}
