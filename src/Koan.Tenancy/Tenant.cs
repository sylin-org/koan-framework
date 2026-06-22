using System;
using Koan.Data.Core;

namespace Koan.Tenancy;

/// <summary>
/// The developer-facing surface for the ambient tenant slice (ARCH-0095) — the curated, typed front door
/// (charter D5) built on the data core's generic ambient carrier (<c>EntityContext.WithSlice</c>/
/// <c>GetSlice</c>, ARCH-0097); the raw <see cref="TenantContext"/> stays hidden behind it. This surface — and
/// all tenancy-related extensions — live in the <c>Koan.Tenancy</c> module, not the data core.
///
/// <para>Lifecycle verbs (<c>Provision</c>/<c>Relocate</c>/<c>Erase</c>/<c>Rename</c>) and the rich
/// registry-backed current-tenant projection (<c>{Id, Codes, Name}</c>) arrive in later slices; this slice is
/// the scoping surface only.</para>
/// </summary>
public static class Tenant
{
    /// <summary>The ambient tenant slice, or <c>null</c> when no tenant is in scope.</summary>
    public static TenantContext? Current => EntityContext.GetSlice<TenantContext>();

    /// <summary>
    /// Scope subsequent entity operations to <paramref name="tenantId"/> for the lifetime of the returned
    /// scope; disposing restores the previous ambient tenant. The explicit, fluent verb. Use for admin, jobs,
    /// tests, and support act-as. Other ambient dimensions (source/adapter/partition/transaction) carry over.
    /// </summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable WithTenant(string tenantId) => EntityContext.WithSlice(TenantContext.For(tenantId));

    /// <summary>Short alias for <see cref="WithTenant"/> — scope subsequent operations to <paramref name="tenantId"/>.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Use(string tenantId) => WithTenant(tenantId);

    /// <summary>
    /// Enter explicit <b>host / control-plane scope</b> — the one loud, audited escape from tenant scoping.
    /// It touches only <c>[HostScoped]</c> entities; a tenant-scoped write under <c>None()</c> still fails
    /// closed unless an explicit allow-unscoped-write capability is present (enforced by the guard slice).
    /// Disposing restores the previous ambient tenant.
    /// </summary>
    public static IDisposable None() => EntityContext.WithSlice(TenantContext.Host);
}
