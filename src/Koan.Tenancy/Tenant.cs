using System;
using Koan.Core.Context;

namespace Koan.Tenancy;

/// <summary>
/// The developer-facing surface for the ambient tenant context (ARCH-0095) — the curated, typed front door over
/// <see cref="KoanContext"/>. The raw <see cref="TenantContext"/> stays behind this business vocabulary, and Core
/// remains unaware of what the value means.
///
/// </summary>
public static class Tenant
{
    /// <summary>The ambient tenant slice, or <c>null</c> when no tenant is in scope.</summary>
    public static TenantContext? Current => KoanContext.Get<TenantContext>();

    /// <summary>
    /// Scope subsequent entity operations to <paramref name="tenantId"/> for the lifetime of the returned
    /// scope; disposing restores the previous ambient tenant. The explicit, fluent verb. Use for trusted admin,
    /// jobs, tests, and support workflows. Other ambient dimensions carry over.
    /// </summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable WithTenant(string tenantId) => KoanContext.Push(TenantContext.For(tenantId));

    /// <summary>Short alias for <see cref="WithTenant"/> — scope subsequent operations to <paramref name="tenantId"/>.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Use(string tenantId) => WithTenant(tenantId);

    /// <summary>
    /// Enter explicit <b>host / control-plane scope</b> — the one loud, audited escape from tenant scoping.
    /// It touches only <c>[HostScoped]</c> entities; a tenant-scoped write under <c>None()</c> still fails
    /// closed unless an explicit allow-unscoped-write capability is present (enforced by the guard slice).
    /// Disposing restores the previous ambient tenant.
    /// </summary>
    public static IDisposable None() => KoanContext.Push(TenantContext.Host);
}
