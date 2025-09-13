using System;
using System.Collections.Generic;

namespace Sora.Data.Core.Relationships
{
    public interface IRelationshipMetadata
    {
    IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType);
    IReadOnlyList<(string PropertyName, Type ChildType)> GetChildRelationships(Type entityType);
    }
}
