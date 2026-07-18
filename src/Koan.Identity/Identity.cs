using System.Diagnostics.CodeAnalysis;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 D1 — the durable <b>person</b>: the canonical subject every <c>…Subject</c> / <c>IdentityId</c> string
/// resolves to. The entity <c>Id</c> is the stable subject (the claims <c>sub</c> on reconciliation, GUID v7
/// otherwise). The ambient <c>Koan.Security.Trust.Identity.Current</c> is the transient
/// <i>claims view</i> of this durable person — a different concern (see the CA1724 suppression below).
/// </summary>
[SuppressMessage("Design", "CA1724:TypeNamesShouldNotMatchNamespaces",
    Justification = "SEC-0007 D1: the durable person entity is intentionally named Identity; the ambient " +
                    "Koan.Security.Trust.Identity is the claims view and is FQN-disambiguated. Renaming to Person " +
                    "was considered and rejected — Identity is the domain noun.")]
public sealed class Identity : Entity<Identity>, IAmbientExempt
{
    /// <summary>Human-facing display name (last writer wins between the user and an IdP backfill — user edits are never clobbered).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Avatar / picture URL.</summary>
    public string? Picture { get; set; }

    /// <summary>Lifecycle status. <see cref="IdentityStatus.Deactivated"/> / <see cref="IdentityStatus.Suspended"/> = cannot act.</summary>
    public IdentityStatus Status { get; set; } = IdentityStatus.Active;

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Updated on every save.</summary>
    [Timestamp(OnSave = true)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True while the person may act.</summary>
    public bool IsActive => Status == IdentityStatus.Active;
}

/// <summary>The lifecycle status of an <see cref="Identity"/>.</summary>
public enum IdentityStatus
{
    /// <summary>Active and able to act.</summary>
    Active = 0,
    /// <summary>Temporarily blocked; recoverable.</summary>
    Suspended = 1,
    /// <summary>Deactivated; Koan cookie sessions reject the person and optional bridges may close other scopes.</summary>
    Deactivated = 2,
}
