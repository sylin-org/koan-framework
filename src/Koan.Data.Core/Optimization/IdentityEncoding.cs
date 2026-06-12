using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Core.Relationships;

namespace Koan.Data.Core.Optimization;

/// <summary>
/// The single source of truth for which of an entity's string members carry GUID-encoded identity
/// values (DATA-0098). A member is GUID-encoded iff it is the entity's <c>Id</c> and the entity's
/// storage strategy is <see cref="StorageOptimizationType.Guid"/>, OR it is a parent reference
/// (<see cref="ParentAttribute"/>) to an entity whose strategy is Guid.
///
/// This is a pure, cached function of the entity <see cref="Type"/> — no value inspection, no DI.
/// Both the write path (per-member serializer registration) and the query path (filter translator)
/// consult it, so they cannot disagree: the write↔read encoding drift class is eliminated by
/// construction. Members it does NOT name are plain strings, stored and queried verbatim.
/// </summary>
public static class IdentityEncoding
{
    private static readonly ConcurrentDictionary<Type, IReadOnlySet<string>> Cache = new();

    /// <summary>The property names on <paramref name="entityType"/> that carry GUID-encoded identity values.</summary>
    public static IReadOnlySet<string> GuidEncodedMembers(Type entityType)
        => Cache.GetOrAdd(entityType, Compute);

    /// <summary>True when <paramref name="propertyName"/> on <paramref name="entityType"/> is a GUID-encoded id/reference.</summary>
    public static bool IsGuidEncoded(Type entityType, string propertyName)
        => GuidEncodedMembers(entityType).Contains(propertyName);

    private static IReadOnlySet<string> Compute(Type entityType)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        // 1. The entity's own Id, when its declared strategy is Guid (Entity<T> / [OptimizeStorage]).
        var keyType = ResolveKeyType(entityType);
        if (keyType is not null)
        {
            var self = StorageOptimizationExtensions.GetStorageOptimization(entityType, keyType);
            if (self.OptimizationType == StorageOptimizationType.Guid)
            {
                members.Add(self.IdPropertyName);
            }
        }

        // 2. Parent references whose target entity's strategy is Guid. The reference field stays a
        //    string in the model; it inherits the referenced entity's identity encoding.
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var parent = prop.GetCustomAttribute<ParentAttribute>(inherit: true);
            if (parent is null)
            {
                continue;
            }

            var parentKey = ResolveKeyType(parent.ParentType);
            if (parentKey is null)
            {
                continue;
            }

            var target = StorageOptimizationExtensions.GetStorageOptimization(parent.ParentType, parentKey);
            if (target.OptimizationType == StorageOptimizationType.Guid)
            {
                members.Add(prop.Name);
            }
        }

        return members;
    }

    private static Type? ResolveKeyType(Type entityType)
    {
        foreach (var iface in entityType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEntity<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
