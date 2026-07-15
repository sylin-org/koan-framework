using System;

namespace Koan.Tenancy;

/// <summary>
/// The immutable ambient <b>tenant context</b> (ARCH-0095), stored as an exact-type value in Core's logical-flow
/// context. Tenancy owns its meaning and developer vocabulary; Core and Data never name tenant fields.
///
/// <para>It is deliberately <b>tri-state</b>, because tenancy needs to distinguish three situations the
/// existing string dimensions cannot:</para>
/// <list type="bullet">
///   <item>no <see cref="TenantContext"/> in scope — <b>no tenant in scope</b> (a tenant-scoped op fails closed);</item>
///   <item><see cref="IsHost"/> is <c>true</c> — <b>explicit host / control-plane scope</b> (the loud
///   <see cref="Tenant.None"/> escape), distinct from "unset";</item>
///   <item><see cref="Id"/> is set — <b>scoped to that immutable tenant surrogate id</b>
///   (<see cref="Tenant.Use"/> / <see cref="Tenant.WithTenant"/>).</item>
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
