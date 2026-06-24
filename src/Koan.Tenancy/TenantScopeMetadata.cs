using System;
using System.Collections.Concurrent;
using System.Reflection;
using Koan.Data.Abstractions;

namespace Koan.Tenancy;

/// <summary>
/// Per-entity-type tenancy metadata (ARCH-0095): performs the exemption reflection scan once per type. The guard
/// caches this per type so the check is cheap on the hot path. A type is exempt from tenant scoping if it carries
/// tenancy's own <c>[HostScoped]</c> control-plane attribute <b>or</b> implements the generic, tenancy-free
/// <see cref="IAmbientExempt"/> marker (ARCH-0100) — the union lets infrastructure (e.g. the <c>Koan.Jobs</c>
/// ledger) opt out without taking a <c>Koan.Tenancy</c> dependency or naming an axis.
/// </summary>
public sealed class TenantScopeMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> HostScopedCache = new();

    /// <summary>True when the entity opts out of tenant scoping (<c>[HostScoped]</c> or <see cref="IAmbientExempt"/>).</summary>
    public bool IsHostScoped { get; }

    /// <summary>Scans the entity type for either exemption signal.</summary>
    public TenantScopeMetadata(Type entityType)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        IsHostScoped = entityType.GetCustomAttribute<HostScopedAttribute>(inherit: true) is not null
                    || typeof(IAmbientExempt).IsAssignableFrom(entityType);
    }

    /// <summary>Cached <c>[HostScoped]</c> check, shared by the guard and the tenant managed-field descriptor.</summary>
    public static bool IsHostScopedType(Type entityType)
        => HostScopedCache.GetOrAdd(entityType, static t => new TenantScopeMetadata(t).IsHostScoped);
}
