using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sora.Data.Core.Relationships
{
    public class RelationshipMetadataService : IRelationshipMetadata
    {
        private static readonly Dictionary<Type, List<(string PropertyName, Type ParentType)>> _parentGraph;
        private static readonly Dictionary<Type, List<(string PropertyName, Type ChildType)>> _childGraph;

        static RelationshipMetadataService()
        {
            _parentGraph = new();
            _childGraph = new();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var allTypes = assemblies.SelectMany(a => {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            }).ToList();

            // Build parent graph
            foreach (var t in allTypes)
            {
                var parentProps = t.GetProperties()
                    .Select(p => (Property: p, Attr: p.GetCustomAttribute<ParentAttribute>()))
                    .Where(x => x.Attr != null)
                    .Select(x => (x.Property.Name, x.Attr.ParentType))
                    .ToList();
                if (parentProps.Count > 0)
                    _parentGraph[t] = parentProps;
            }

            // Build child graph by scanning for [Parent] attributes referencing each type
            foreach (var t in allTypes)
            {
                foreach (var other in allTypes)
                {
                    foreach (var prop in other.GetProperties())
                    {
                        var parentAttr = prop.GetCustomAttribute<ParentAttribute>();
                        if (parentAttr != null && parentAttr.ParentType == t)
                        {
                            if (!_childGraph.TryGetValue(t, out var list))
                            {
                                list = new List<(string PropertyName, Type ChildType)>();
                                _childGraph[t] = list;
                            }
                            list.Add((prop.Name, other));
                        }
                    }
                }
            }
        }

        public IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType)
        {
            return _parentGraph.TryGetValue(entityType, out var rels) ? rels : Array.Empty<(string, Type)>();
        }

        public IReadOnlyList<(string PropertyName, Type ChildType)> GetChildRelationships(Type entityType)
        {
            return _childGraph.TryGetValue(entityType, out var rels) ? rels : Array.Empty<(string, Type)>();
        }
    }
}
