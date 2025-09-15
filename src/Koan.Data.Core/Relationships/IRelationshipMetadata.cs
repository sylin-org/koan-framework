using System;
using System.Collections.Generic;

namespace Koan.Data.Core.Relationships
{
    public interface IRelationshipMetadata
    {
        // Parent relationships
        IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType);
        IReadOnlyList<Type> GetAllParentTypes(Type entityType);
        bool HasSingleParent(Type entityType);

        // Child relationships
        IReadOnlyList<(string ReferenceProperty, Type ChildType)> GetChildRelationships(Type parentType);
        IReadOnlyList<Type> GetAllChildTypes(Type parentType);
        bool HasSingleChildType(Type entityType);
        bool HasSingleChildRelationship<TChild>(Type entityType);

        // Validation
        void ValidateRelationshipCardinality(Type entityType, string operation);
    }
}
