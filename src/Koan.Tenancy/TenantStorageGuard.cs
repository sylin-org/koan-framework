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
        if (TenancyAmbient.HasEffectiveTenant()) return;               // a concrete tenant (explicit or dev-fallback) is in scope

        // A tenant-scoped operation with no concrete tenant in scope (unset, or explicit host scope). The
        // diagnostic names the exact fix (Redis protected-mode quality, ARCH-0099 §1).
        var message = TenancyRefusal.NoTenantInScope(entityType.Name);

        if (_runtime.Posture == TenancyPosture.Open)
        {
            _logger.LogWarning("Tenancy guard (dev-open): {Message}", message);
            return;
        }

        throw new InvalidOperationException(message); // Closed — fail closed
    }
}
