using System;
using System.Reflection;
using Koan.Data.Core.Tenancy;

namespace Koan.Data.Core.Metadata;

/// <summary>
/// Caches per-entity-type tenancy metadata (ARCH-0095). Performs the reflection scan once per entity type;
/// cached at <c>RepositoryFacade</c> construction for hot-path evaluation (mirrors
/// <see cref="TimestampPropertyBag"/>). v1 holds the host-scoped flag; the shared-schema discriminator
/// accessor lands here in the read-filter/write-stamp slice (ARCH-0095 §12).
/// </summary>
public sealed class TenantScopeMetadata
{
    /// <summary>True when the entity is <c>[HostScoped]</c> — it opts out of tenant scoping.</summary>
    public bool IsHostScoped { get; }

    /// <summary>Scans the entity type for tenancy attributes. Executes once per type at facade construction.</summary>
    public TenantScopeMetadata(Type entityType)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        IsHostScoped = entityType.GetCustomAttribute<HostScopedAttribute>(inherit: true) is not null;
    }
}
