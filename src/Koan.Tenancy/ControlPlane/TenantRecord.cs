using System;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// The durable control-plane registry row for a tenant (ARCH-0099 §2/§4a) — a dogfooded <c>[HostScoped]</c>
/// <see cref="Entity{T}"/>: it lives in the root/host scope, exempt from tenant isolation and never prefixed
/// (§4a), so the registry that defines tenants is not itself tenant-scoped. The immutable GUID-v7 surrogate
/// <c>Id</c> is the stable handle; the display <see cref="Name"/> is mutable, so a rename never moves storage.
/// (Mutable resolution codes/domains are separate keyed entities — a later slice.) The ambient scoping surface
/// is the static <see cref="Tenant"/>; this is the persisted record behind it.
/// </summary>
[HostScoped]
public sealed class TenantRecord : Entity<TenantRecord>
{
    /// <summary>The mutable display name (e.g. "Acme"). Renaming never moves storage — the id is the handle.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional routing slug (e.g. <c>acme</c>) — the human-facing handle the subdomain (<c>{code}.host</c>) and
    /// path (<c>/t/{code}</c>) tenant carriers resolve to this tenant's <c>Id</c> (ARCH-0099 §1 / SEC-0007 P4). The
    /// claim and header carriers use the <c>Id</c> directly. Empty = not reachable by slug (id-only). Renaming the
    /// slug never moves storage — the immutable <c>Id</c> remains the handle. Uniqueness is the app's to enforce.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Lifecycle status; defaults to <see cref="TenantStatus.Active"/>.</summary>
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }
}
