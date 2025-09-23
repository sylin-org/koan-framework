using System;
using Koan.Data.Abstractions;

namespace Koan.Web.Endpoints;

public interface IEntityEndpointDescriptorProvider
{
    EntityEndpointDescriptor Describe<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    EntityEndpointDescriptor Describe(Type entityType, Type keyType);
}
