using System;
using System.Collections.Concurrent;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Tenancy;

/// <summary>
/// The tenant fail-closed gate (ARCH-0095 P1, charter L6/C5) — registered as a generic
/// <see cref="IStorageGuard"/> contributor (DATA-0105 §0) so the data-core chokepoint invokes it without
/// naming a tenant. Reads the <see cref="TenancyOptions"/> posture and the ambient <see cref="Tenant"/> slice,
/// computing <c>[HostScoped]</c> itself (cached per type); the error <b>names the fix</b> (charter L6) rather
/// than throwing a bare exception deep in business logic.
/// </summary>
internal sealed class TenantStorageGuard : IStorageGuard
{
    private static readonly ConcurrentDictionary<Type, bool> HostScopedCache = new();

    private readonly IOptions<TenancyOptions> _options;
    private readonly ILogger<TenantStorageGuard> _logger;

    public TenantStorageGuard(IOptions<TenancyOptions> options, ILogger<TenantStorageGuard> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Guard(Type entityType)
    {
        var mode = _options.Value.Mode;
        if (mode == TenancyMode.Off) return;            // tenancy disabled (default) → no-op, zero regression
        if (IsHostScoped(entityType)) return;           // [HostScoped] entity → not tenant-scoped
        if (Tenant.Current?.HasTenant == true) return;  // a concrete tenant is in scope → allowed

        // A tenant-scoped operation with no concrete tenant in scope (unset, or explicit host scope).
        var message =
            $"No tenant in scope for tenant-scoped '{entityType.Name}'. Wrap the call in " +
            $"'using (Tenant.Use(id))', configure tenant resolution, or mark the entity [HostScoped].";

        if (mode == TenancyMode.Warn)
        {
            _logger.LogWarning("Tenancy guard (warn): {Message}", message);
            return;
        }

        throw new InvalidOperationException(message); // Enforce — fail closed
    }

    private static bool IsHostScoped(Type entityType)
        => HostScopedCache.GetOrAdd(entityType, static t => new TenantScopeMetadata(t).IsHostScoped);
}
