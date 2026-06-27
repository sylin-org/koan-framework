using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Koan.Data.Access;

/// <summary>The resolved, canonical access-scoping descriptor for an entity type (the normalized field + the prefix).</summary>
internal sealed record AccessScopedInfo(string Field, string ScopePrefix);

/// <summary>Type-plane memoized probe + normalizer for the <see cref="AccessScopedAttribute"/> — cheap on the hot read path.</summary>
internal static class AccessScopedMetadata
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
    private static readonly ConcurrentDictionary<Type, AccessScopedInfo?> _cache = new();

    /// <summary>
    /// The entity's resolved <see cref="AccessScopedInfo"/>, or <c>null</c> when it is not access-scoped. The
    /// <see cref="AccessScopedAttribute.Field"/> is normalized to the actual CLR property name (case-insensitive
    /// lookup) so the emitted filter uses the canonical name every adapter resolves — a lower-cased <c>Field</c> on a
    /// PascalCase property would otherwise silently deny-ALL on a case-sensitive relational JSON path (SEC-0008
    /// review). A <c>Field</c> that names no public instance property fails loud here (never a silent total deny).
    /// </summary>
    public static AccessScopedInfo? For(Type entityType)
        => _cache.GetOrAdd(entityType, static t =>
        {
            var attr = t.GetCustomAttribute<AccessScopedAttribute>(inherit: true);
            if (attr is null) return null;
            var prop = t.GetProperty(attr.Field, Flags)
                ?? throw new InvalidOperationException(
                    $"[AccessScoped] on '{t.FullName}' names field '{attr.Field}', which is not a public instance property. " +
                    "Field must be the entity's CLR property name (e.g. nameof(MyEntity.EventId)).");
            return new AccessScopedInfo(prop.Name, attr.ScopePrefix);
        });

    /// <summary>True when the entity carries <see cref="AccessScopedAttribute"/> (the axis activation predicate).</summary>
    public static bool IsAccessScoped(Type entityType) => For(entityType) is not null;
}
