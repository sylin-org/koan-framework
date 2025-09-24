using System.Linq;
using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Web.Endpoints;

internal sealed class DefaultEntityEndpointDescriptorProvider : IEntityEndpointDescriptorProvider
{
    private readonly IOptions<EntityEndpointOptions> _options;
    private readonly ConcurrentDictionary<(Type Entity, Type Key), EntityEndpointDescriptor> _cache = new();

    public DefaultEntityEndpointDescriptorProvider(IOptions<EntityEndpointOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public EntityEndpointDescriptor Describe<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => Describe(typeof(TEntity), typeof(TKey));

    public EntityEndpointDescriptor Describe(Type entityType, Type keyType)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        if (keyType is null) throw new ArgumentNullException(nameof(keyType));

        return _cache.GetOrAdd((entityType, keyType), static (key, state) => state.BuildDescriptor(key.Entity, key.Key), this);
    }

    private EntityEndpointDescriptor BuildDescriptor(Type entityType, Type keyType)
    {
        var options = _options.Value;
        var metadata = new EntityEndpointDescriptorMetadata
        {
            DefaultPageSize = options.DefaultPageSize,
            MaxPageSize = options.MaxPageSize,
            DefaultView = options.DefaultView,
            AllowRelationshipExpansion = options.AllowRelationshipExpansion,
            AllowedShapes = (options.AllowedShapes ?? new List<string>()).ToArray()
        };

        var supportsRelationships = metadata.AllowRelationshipExpansion;
        var supportsShape = metadata.AllowedShapes.Count > 0;

        var operations = new List<EntityEndpointOperationDescriptor>
        {
            new()
            {
                Kind = EntityEndpointOperationKind.Collection,
                ReturnsCollection = true,
                RequiresBody = false,
                SupportsDatasetRouting = true,
                SupportsRelationships = supportsRelationships,
                SupportsShape = supportsShape,
                SupportsQueryFiltering = true
            },
            new()
            {
                Kind = EntityEndpointOperationKind.Query,
                ReturnsCollection = true,
                RequiresBody = true,
                SupportsDatasetRouting = true,
                SupportsRelationships = supportsRelationships,
                SupportsShape = supportsShape,
                SupportsQueryFiltering = true
            },
            new()
            {
                Kind = EntityEndpointOperationKind.GetNew,
                ReturnsCollection = false,
                RequiresBody = false,
                SupportsDatasetRouting = false,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.GetById,
                ReturnsCollection = false,
                RequiresBody = false,
                SupportsDatasetRouting = true,
                SupportsRelationships = supportsRelationships,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.Upsert,
                ReturnsCollection = false,
                RequiresBody = true,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.UpsertMany,
                ReturnsCollection = false,
                RequiresBody = true,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.Delete,
                ReturnsCollection = false,
                RequiresBody = false,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.DeleteMany,
                ReturnsCollection = false,
                RequiresBody = true,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.DeleteByQuery,
                ReturnsCollection = false,
                RequiresBody = false,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = true
            },
            new()
            {
                Kind = EntityEndpointOperationKind.DeleteAll,
                ReturnsCollection = false,
                RequiresBody = false,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            },
            new()
            {
                Kind = EntityEndpointOperationKind.Patch,
                ReturnsCollection = false,
                RequiresBody = true,
                SupportsDatasetRouting = true,
                SupportsRelationships = false,
                SupportsShape = false,
                SupportsQueryFiltering = false
            }
        };

        return new EntityEndpointDescriptor(entityType, keyType, operations, metadata);
    }
}
