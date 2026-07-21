using System;
using System.Collections.Generic;
using Koan.Core.Hosting.Registry;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§B) — maps each entity type to its discovered <see cref="EntityAccess{TEntity}"/> realization. Built
/// once at boot from <see cref="KoanRegistry"/>'s <c>[KoanDiscoverable]</c> <see cref="IEntityAccessRealization"/>
/// implementers (the same discovery authority every Koan contract uses — no bespoke assembly scan). Two
/// realizations targeting one entity is a fail-fast configuration error.
/// </summary>
public sealed class EntityAccessRegistry
{
    private readonly IReadOnlyDictionary<Type, Type> _byEntity;

    private EntityAccessRegistry(IReadOnlyDictionary<Type, Type> byEntity) => _byEntity = byEntity;

    /// <summary>The realization type for <paramref name="entityType"/>, or <c>null</c> when none is declared.</summary>
    public Type? For(Type entityType) => _byEntity.TryGetValue(entityType, out var t) ? t : null;

    /// <summary>All discovered (entity → realization) pairs (for DI registration).</summary>
    public IEnumerable<KeyValuePair<Type, Type>> All() => _byEntity;

    public static EntityAccessRegistry FromDiscovery()
        => FromImplementors(KoanRegistry.GetDiscoveredImplementors(typeof(IEntityAccessRealization)));

    /// <summary>Build from an explicit realization list (the testable seam <see cref="FromDiscovery"/> delegates to).</summary>
    public static EntityAccessRegistry FromImplementors(IEnumerable<Type> realizations)
    {
        var map = new Dictionary<Type, Type>();
        foreach (var realization in realizations)
        {
            if (!realization.IsClass || realization.IsAbstract || realization.IsGenericTypeDefinition) continue;
            var entity = ExtractEntityType(realization);
            if (entity is null) continue;
            if (realization.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new AccessGateException(
                    $"EntityAccess realization {realization.FullName} must have a public parameterless constructor " +
                    "(its principal-independent gate is read on a throwaway instance at boot).");
            }
            if (map.TryGetValue(entity, out var existing))
            {
                throw new AccessGateException(
                    $"Two EntityAccess realizations target {entity.Name}: {existing.FullName} and " +
                    $"{realization.FullName}. An entity may have at most one realization.");
            }
            map[entity] = realization;
        }
        return new EntityAccessRegistry(map);
    }

    // The entity is the TEntity of the closed EntityAccess<TEntity> in the realization's base chain.
    private static Type? ExtractEntityType(Type realizationType)
    {
        for (var t = realizationType.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EntityAccess<>))
            {
                var entity = t.GetGenericArguments()[0];
                return entity.IsGenericParameter ? null : entity;
            }
        }
        return null;
    }
}
