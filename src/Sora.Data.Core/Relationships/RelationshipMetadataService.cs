using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sora.Data.Core.Relationships
{
    public class RelationshipMetadataService : IRelationshipMetadata
    {
        public IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType)
        {
            var parentProps = entityType.GetProperties()
                .Select(p => (Property: p, Attr: p.GetCustomAttribute<ParentAttribute>()))
                .Where(x => x.Attr != null)
                .Select(x => (x.Property.Name, x.Attr.ParentType))
                .ToList();
            return parentProps;
        }
    }
}
