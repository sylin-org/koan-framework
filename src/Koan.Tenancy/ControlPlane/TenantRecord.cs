using System;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// The durable control-plane registry row for a tenant — a dogfooded <c>[HostScoped]</c>
/// <see cref="Entity{T}"/>: it lives in the root/host scope, exempt from tenant isolation and never prefixed
/// (§4a), so the registry that defines tenants is not itself tenant-scoped. The immutable GUID-v7 surrogate
/// <c>Id</c> is the stable handle; the display <see cref="Name"/> is mutable, so a rename never moves storage.
/// The ambient scoping surface is the static <see cref="Tenant"/>; this is the persisted record behind it.
/// </summary>
[HostScoped]
public sealed class TenantRecord : Entity<TenantRecord>
{
    /// <summary>The mutable display name (e.g. "Acme"). Renaming never moves storage — the id is the handle.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional routing slug (e.g. <c>acme</c>) — the human-facing handle the subdomain (<c>{code}.host</c>) and
    /// path (<c>/t/{code}</c>) tenant carriers resolve to this tenant's <c>Id</c>. The
    /// claim and header carriers use the <c>Id</c> directly. Empty = not reachable by slug (id-only). Renaming the
    /// slug never moves storage — the immutable <c>Id</c> remains the handle. The supported administration path
    /// rejects duplicates; inbound resolution fails closed if direct Entity writes still create ambiguity.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }
}
