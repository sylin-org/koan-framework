using System;

namespace Koan.Data.Core.Tenancy;

/// <summary>
/// The immutable ambient <b>tenant slice</b> (ARCH-0095) — the flagship typed slice carried on the one
/// Facet-3 ambient carrier (<see cref="EntityContext"/>). Charter L1 forbids a second ambient mechanism, so
/// the tenant rides the existing carrier; charter L2/L4 make it immutable and restore-on-dispose.
///
/// <para>It is deliberately <b>tri-state</b>, because tenancy needs to distinguish three situations the
/// existing string dimensions cannot:</para>
/// <list type="bullet">
///   <item>the carrier value is <c>null</c> — <b>no tenant in scope</b> (a tenant-scoped op fails closed);</item>
///   <item><see cref="IsHost"/> is <c>true</c> — <b>explicit host / control-plane scope</b> (the loud
///   <see cref="Tenant.None"/> escape), distinct from "unset";</item>
///   <item><see cref="Id"/> is set — <b>scoped to that immutable tenant surrogate id</b>
///   (<see cref="Tenant.Use"/>).</item>
/// </list>
/// </summary>
public sealed record TenantContext
{
    private TenantContext() { }

    /// <summary>The immutable tenant surrogate id (e.g. <c>"a1b2c3"</c>); <c>null</c> when host-scoped.</summary>
    public string? Id { get; private init; }

    /// <summary>True for the explicit host / control-plane scope (<see cref="Tenant.None"/>).</summary>
    public bool IsHost { get; private init; }

    /// <summary>True when a concrete tenant is in scope (not host, not unset).</summary>
    public bool HasTenant => Id is not null;

    /// <summary>The shared, explicit host-scope value.</summary>
    public static readonly TenantContext Host = new() { IsHost = true };

    /// <summary>Scope to a concrete, immutable tenant id. Validates at the boundary — a blank id is never a scope.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static TenantContext For(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A tenant id must be a non-empty value.", nameof(tenantId));
        return new TenantContext { Id = tenantId };
    }
}
