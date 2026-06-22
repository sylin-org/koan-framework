using System;

namespace Koan.Tenancy;

/// <summary>
/// Marks an entity as <b>host-scoped</b> — it opts <i>out</i> of tenant scoping (ARCH-0095). Once tenancy is
/// on, entities are tenant-scoped by default (secure-by-default); a <c>[HostScoped]</c> entity is the quiet,
/// legitimate system/control-plane exception (registry rows, audit, identity), distinct from the loud, audited
/// ad-hoc escape <see cref="Tenant.None"/>. The chokepoint guard skips host-scoped entities.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class HostScopedAttribute : Attribute
{
}
