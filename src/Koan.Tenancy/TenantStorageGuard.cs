using System;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Koan.Tenancy;

/// <summary>
/// The tenant fail-closed gate (ARCH-0095 P1 / ARCH-0099 §1, charter L6/C5) — registered as a generic
/// <see cref="IStorageGuard"/> contributor (DATA-0105 §0) so the data-core chokepoint invokes it without naming
/// a tenant. Reads the resolved <see cref="TenancyRuntime.Posture"/> and the ambient <see cref="Tenant"/> slice,
/// computing <c>[HostScoped]</c> itself (cached per type); the error <b>names the fix</b> (charter L6) rather
/// than throwing a bare exception deep in business logic. <see cref="TenancyPosture.Open"/> (dev) warns and
/// proceeds; <see cref="TenancyPosture.Closed"/> (prod/ambiguous) fails closed.
/// </summary>
internal sealed class TenantStorageGuard : IStorageGuard
{
    private readonly TenancyRuntime _runtime;
    private readonly ILogger<TenantStorageGuard> _logger;

    public TenantStorageGuard(TenancyRuntime runtime, ILogger<TenantStorageGuard> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Guard(Type entityType)
    {
        if (TenantScopeMetadata.IsHostScopedType(entityType)) return;   // [HostScoped] entity → not tenant-scoped
        if (Tenant.Current?.HasTenant == true) return;                 // a concrete tenant is in scope → allowed

        // A tenant-scoped operation with no concrete tenant in scope (unset, or explicit host scope).
        var message =
            $"No tenant in scope for tenant-scoped '{entityType.Name}'. Wrap the call in " +
            $"'using (Tenant.Use(id))', configure tenant resolution, or mark the entity [HostScoped].";

        if (_runtime.Posture == TenancyPosture.Open)
        {
            _logger.LogWarning("Tenancy guard (dev-open): {Message}", message);
            return;
        }

        throw new InvalidOperationException(message); // Closed — fail closed
    }
}
