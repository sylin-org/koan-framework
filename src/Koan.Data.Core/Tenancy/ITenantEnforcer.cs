using System;

namespace Koan.Data.Core.Tenancy;

/// <summary>
/// The fail-closed gate of the chokepoint guard (ARCH-0095 P1). Consulted by <c>RepositoryFacade</c> on
/// every operation: when tenancy is on and the entity is tenant-scoped, a missing tenant is blocked
/// (<see cref="TenancyMode.Enforce"/>) or logged (<see cref="TenancyMode.Warn"/>). When tenancy is off it
/// is a no-op, so a non-tenant app behaves identically. The read-filter and write-stamp (the predicate
/// injection and the tenant discriminator) are added in the next slice.
/// </summary>
public interface ITenantEnforcer
{
    /// <summary>
    /// Enforce the tenant gate for an operation on <paramref name="entityType"/>. Throws (Enforce) or logs
    /// (Warn) when the entity is tenant-scoped and no concrete tenant is in scope; otherwise returns.
    /// </summary>
    /// <param name="entityType">The entity being operated on (used for the fix-naming message).</param>
    /// <param name="isHostScoped">Whether the entity is <c>[HostScoped]</c> (cached by the facade).</param>
    /// <exception cref="InvalidOperationException">
    /// Mode is <see cref="TenancyMode.Enforce"/>, the entity is tenant-scoped, and no tenant is in scope.
    /// </exception>
    void Guard(Type entityType, bool isHostScoped);
}
