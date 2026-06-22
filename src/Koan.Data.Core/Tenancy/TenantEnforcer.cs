using System;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Tenancy;

/// <summary>
/// Default <see cref="ITenantEnforcer"/> — the fail-closed gate (ARCH-0095 P1, charter L6/C5). Reads the
/// <see cref="TenancyOptions"/> posture and the ambient <see cref="Tenant"/> slice; the error <b>names the
/// fix</b> (charter L6) rather than throwing a bare exception deep in business logic.
/// </summary>
internal sealed class TenantEnforcer : ITenantEnforcer
{
    private readonly IOptions<TenancyOptions> _options;
    private readonly ILogger<TenantEnforcer> _logger;

    public TenantEnforcer(IOptions<TenancyOptions> options, ILogger<TenantEnforcer> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Guard(Type entityType, bool isHostScoped)
    {
        var mode = _options.Value.Mode;
        if (mode == TenancyMode.Off) return;            // tenancy disabled (default) → no-op, zero regression
        if (isHostScoped) return;                       // [HostScoped] entity → not tenant-scoped
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
}
