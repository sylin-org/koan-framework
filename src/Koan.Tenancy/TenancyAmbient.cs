using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tenancy;

/// <summary>
/// The single resolution point for the effective ambient tenant (ARCH-0099 §1) — used by both the fail-closed
/// gate and the <c>__koan_tenant</c> write-stamp so they never disagree. An explicit ambient slice always wins
/// (<c>Tenant.Use</c> → that id; <c>Tenant.None()</c> → host scope, no tenant). Only when the slice is genuinely
/// <b>unset</b> does it consult the running host's dev-open fallback (resolved per-op from
/// <see cref="AppHost.Current"/>, the established per-host ambient pattern) — non-null only in dev, so prod and
/// non-seeded hosts never fall back.
/// </summary>
internal static class TenancyAmbient
{
    /// <summary>The effective tenant id, or <c>null</c> when no tenant is in scope (unset with no dev fallback, or explicit host scope).</summary>
    public static string? EffectiveTenantId()
    {
        var slice = Tenant.Current;
        if (slice is not null) return slice.IsHost ? null : slice.Id;   // explicit scope wins
        return AppHost.Current?.GetService<TenancyDevState>()?.FallbackTenantId; // unset → dev-open fallback
    }

    /// <summary>True when a concrete tenant (explicit or dev-fallback) is in scope.</summary>
    public static bool HasEffectiveTenant() => EffectiveTenantId() is not null;
}
