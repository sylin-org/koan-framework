using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tenancy;

/// <summary>
/// The single resolution point for the effective ambient tenant (ARCH-0099 §1), consumed by Tenancy's one Core
/// segmentation contribution. Active pillars compile their own physical realization from that meaning. An explicit ambient slice always wins
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
}
