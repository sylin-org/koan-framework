using System;
using System.Collections.Generic;

namespace Koan.Web.Endpoints;

public sealed class EntityEndpointDescriptor
{
    public EntityEndpointDescriptor(Type entityType, Type keyType, IReadOnlyList<EntityEndpointOperationDescriptor> operations, EntityEndpointDescriptorMetadata metadata)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
        Operations = operations ?? throw new ArgumentNullException(nameof(operations));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public Type EntityType { get; }

    public Type KeyType { get; }

    public IReadOnlyList<EntityEndpointOperationDescriptor> Operations { get; }

    public EntityEndpointDescriptorMetadata Metadata { get; }
}
