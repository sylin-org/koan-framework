using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Tenancy;

namespace SnapVault.Models;

/// <summary>
/// The fail-closed source of a guest's scoped access to one shareable set (an <see cref="Event"/>). Minted by an
/// explicit studio grant; the Web context contributor validates the link selector against this row once per request
/// and contributes the corresponding PhotoAsset predicate.
/// <para>
/// [HostScoped] (like <c>Membership</c>) so it is resolvable from the request contributor without an ambient
/// studio tenant; <see cref="StudioTenantId"/> carries the scope explicitly. Revocation is immediate — the hook
/// re-reads grants per request, so removing this row fail-closes the next read.
/// </para>
/// </summary>
[HostScoped]
public sealed class GalleryGrant : Entity<GalleryGrant>
{
    public static class Permission
    {
        public const string View = "view";
        public const string Select = "select";
        public const string Comment = "comment";
    }

    public static class Template
    {
        public const string Viewer = "viewer";
        public const string Proofer = "proofer";
    }

    public const string TenantRole = "guest";

    /// <summary>The canonical guest person (Identity id) — never an email string.</summary>
    public string IdentityId { get; set; } = "";

    /// <summary>The granted set (an <see cref="Event"/> id). Guest reads are narrowed to PhotoAsset.EventId == this.</summary>
    public string EventId { get; set; } = "";

    /// <summary>The studio (a TenantRecord id) the set belongs to.</summary>
    public string StudioTenantId { get; set; } = "";

    /// <summary>Guest permissions: "view" (always), optionally "select" / "comment" (the proofing affordances).</summary>
    public List<string> Permissions { get; set; } = new() { Permission.View };

    /// <summary>Optional expiry; null = no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Soft on/off independent of expiry.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True when the grant is active, unexpired, and carries <paramref name="permission"/> (case-insensitive
    /// — a casing drift on this security-critical list must not silently deny).</summary>
    public bool Allows(string permission)
        => IsActive
           && Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)
           && (ExpiresAt is null || DateTimeOffset.UtcNow < ExpiresAt);

    /// <summary>Deterministic id — one grant per (guest, event) so command retries converge instead of duplicating.
    /// INVARIANT: <paramref name="eventId"/> is globally unique; the pair is both the durable grant identity and the
    /// server-side lookup used to validate a gallery link.</summary>
    public static string KeyFor(string identityId, string eventId) => DeterministicId.From(identityId, eventId);
}
