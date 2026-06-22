using System;

namespace Koan.Data.Core.Tenancy;

/// <summary>
/// The developer-facing surface for the ambient tenant slice (ARCH-0095) — the curated, typed front door
/// (charter D5) over the one carrier (<see cref="EntityContext"/>); the raw <see cref="TenantContext"/>
/// stays hidden behind it. This is the <b>tenancy kernel</b> surface and lives in <c>Koan.Data.Core</c>
/// (ARCH-0095 §3a: P1–P3+P7 at the <c>Koan.Data</c> level, no separate SKU).
///
/// <para>Lifecycle verbs (<c>Provision</c>/<c>Relocate</c>/<c>Erase</c>/<c>Rename</c>) and the rich
/// registry-backed current-tenant projection (<c>{Id, Codes, Name}</c>) arrive in later slices; this slice
/// is the scoping surface only.</para>
/// </summary>
public static class Tenant
{
    /// <summary>The ambient tenant slice, or <c>null</c> when no tenant is in scope.</summary>
    public static TenantContext? Current => EntityContext.GetSlice<TenantContext>();

    /// <summary>
    /// Scope subsequent entity operations to <paramref name="tenantId"/> for the lifetime of the returned
    /// scope; disposing restores the previous ambient tenant. Use for admin, jobs, tests, and support
    /// act-as. Other ambient dimensions (source/adapter/partition/transaction) carry over unchanged.
    /// </summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Use(string tenantId) => EntityContext.WithSlice(TenantContext.For(tenantId));

    /// <summary>
    /// Enter explicit <b>host / control-plane scope</b> — the one loud, audited escape from tenant scoping.
    /// It touches only <c>[HostScoped]</c> entities; a tenant-scoped write under <c>None()</c> still fails
    /// closed unless an explicit allow-unscoped-write capability is present (enforced by the guard slice).
    /// Disposing restores the previous ambient tenant.
    /// </summary>
    public static IDisposable None() => EntityContext.WithSlice(TenantContext.Host);
}
