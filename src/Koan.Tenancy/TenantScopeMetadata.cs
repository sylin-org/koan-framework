using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Koan.Tenancy;

/// <summary>
/// Per-entity-type tenancy metadata (ARCH-0095): performs the <c>[HostScoped]</c> reflection scan once per
/// type. The guard caches this per type so the check is cheap on the hot path.
/// </summary>
public sealed class TenantScopeMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> HostScopedCache = new();

    /// <summary>True when the entity is <c>[HostScoped]</c> — it opts out of tenant scoping.</summary>
    public bool IsHostScoped { get; }

    /// <summary>Scans the entity type for the host-scoped marker.</summary>
    public TenantScopeMetadata(Type entityType)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        IsHostScoped = entityType.GetCustomAttribute<HostScopedAttribute>(inherit: true) is not null;
    }

    /// <summary>Cached <c>[HostScoped]</c> check, shared by the guard and the tenant managed-field descriptor.</summary>
    public static bool IsHostScopedType(Type entityType)
        => HostScopedCache.GetOrAdd(entityType, static t => new TenantScopeMetadata(t).IsHostScoped);
}
