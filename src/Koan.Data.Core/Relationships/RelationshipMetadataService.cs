using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Relationships
{
    public class RelationshipMetadataService : IRelationshipMetadata
    {
        private readonly ConcurrentDictionary<Type, IReadOnlyList<(string, Type)>> _parentCache = new();
        private readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> _parentTypesCache = new();
        private readonly ConcurrentDictionary<Type, IReadOnlyList<(string, Type)>> _childCache = new();
        private readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> _childTypesCache = new();

        public IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType)
        {
            return _parentCache.GetOrAdd(entityType, static type =>
            {
                var parentProps = type.GetProperties()
                    .Select(p => (Property: p, Attr: p.GetCustomAttribute<ParentAttribute>()))
                    .Where(x => x.Attr != null)
                    .Select(x => (x.Property.Name, x.Attr!.ParentType))
                    .ToList();
                return parentProps.AsReadOnly();
            });
        }

        public IReadOnlyList<Type> GetAllParentTypes(Type entityType)
        {
            return _parentTypesCache.GetOrAdd(entityType, type =>
            {
                var parentTypes = GetParentRelationships(type)
                    .Select(x => x.ParentType)
                    .Distinct()
                    .ToList();
                return parentTypes.AsReadOnly();
            });
        }

        public bool HasSingleParent(Type entityType)
        {
            var parentRelationships = GetParentRelationships(entityType);
            return parentRelationships.Count == 1;
        }

        public IReadOnlyList<(string ReferenceProperty, Type ChildType)> GetChildRelationships(Type parentType)
        {
            return _childCache.GetOrAdd(parentType, static type =>
            {
                var childProps = DiscoverChildRelationships(type);
                return childProps.AsReadOnly();
            });
        }

        public IReadOnlyList<Type> GetAllChildTypes(Type parentType)
        {
            return _childTypesCache.GetOrAdd(parentType, type =>
            {
                var childTypes = GetChildRelationships(type)
                    .Select(x => x.ChildType)
                    .Distinct()
                    .ToList();
                return childTypes.AsReadOnly();
            });
        }

        public bool HasSingleChildType(Type entityType)
        {
            var childTypes = GetAllChildTypes(entityType);
            return childTypes.Count == 1;
        }

        public bool HasSingleChildRelationship<TChild>(Type entityType)
        {
            var childRelationships = GetChildRelationships(entityType)
                .Where(x => x.ChildType == typeof(TChild))
                .ToList();
            return childRelationships.Count == 1;
        }

        public void ValidateRelationshipCardinality(Type entityType, string operation)
        {
            switch (operation.ToLowerInvariant())
            {
                case "getparent":
                    if (!HasSingleParent(entityType))
                    {
                        var parentCount = GetParentRelationships(entityType).Count;
                        if (parentCount == 0)
                            throw new InvalidOperationException($"{entityType.Name} has no parent relationships defined");
                        else
                            throw new InvalidOperationException($"{entityType.Name} has multiple parents ({parentCount}). Use GetParents() or GetParent<T>() instead");
                    }
                    break;

                case "getchildren":
                    if (!HasSingleChildType(entityType))
                    {
                        var childTypeCount = GetAllChildTypes(entityType).Count;
                        if (childTypeCount == 0)
                            throw new InvalidOperationException($"{entityType.Name} has no child relationships defined");
                        else
                            throw new InvalidOperationException($"{entityType.Name} has multiple child types ({childTypeCount}). Use GetChildren<T>() or GetChildren<T>(propertyName) instead");
                    }
                    break;

                case "getchildren<t>":
                    // Generic method validation would need runtime type information
                    break;

                default:
                    throw new ArgumentException($"Unknown relationship operation: {operation}");
            }
        }

        private static List<(string ReferenceProperty, Type ChildType)> DiscoverChildRelationships(Type parentType)
        {
            var childRelationships = new List<(string, Type)>();

            // Find all types in cached assemblies that have ParentAttribute pointing to parentType
            // Use cached assemblies instead of bespoke AppDomain scanning
            var assemblies = AssemblyCache.Instance.GetAllAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract);

                    foreach (var type in types)
                    {
                        // Skip if not an entity type
                        if (!typeof(IEntity<>).IsAssignableFromGeneric(type))
                            continue;

                        var properties = type.GetProperties()
                            .Where(p => p.GetCustomAttribute<ParentAttribute>()?.ParentType == parentType);

                        foreach (var property in properties)
                        {
                            childRelationships.Add((property.Name, type));
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
                catch (Exception)
                {
                    // Skip other assembly loading issues
                }
            }

            return childRelationships;
        }
    }

    // Helper extension method for generic type checking
    internal static class TypeExtensions
    {
        public static bool IsAssignableFromGeneric(this Type genericType, Type type)
        {
            if (!genericType.IsGenericTypeDefinition)
                return genericType.IsAssignableFrom(type);

            var baseType = type.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == genericType)
                    return true;
                baseType = baseType.BaseType;
            }

            return type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericType);
        }
    }
}
