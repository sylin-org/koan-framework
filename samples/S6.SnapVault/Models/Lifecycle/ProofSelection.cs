using System;
using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Tenancy;

namespace S6.SnapVault.Models;

/// <summary>
/// A guest's proofing mark on one photo within a shared set — favorite / rating / "I select this" / a comment.
/// Deliberately SEPARATE from <see cref="PhotoAsset.IsFavorite"/> and <see cref="PhotoAsset.Rating"/> (which are the
/// STUDIO's own marks): the client's picks must never overwrite the studio's, and the studio reads the client's
/// selections back as a distinct view.
/// <para>
/// [HostScoped] control-plane row: the guest (no ambient studio tenant) is the writer; <see cref="StudioTenantId"/> +
/// <see cref="EventId"/> carry the scope explicitly. Reads always filter by those + <see cref="GuestIdentityId"/>.
/// </para>
/// </summary>
[HostScoped]
public sealed class ProofSelection : Entity<ProofSelection>
{
    /// <summary>The canonical guest person (Identity id) who made the mark.</summary>
    public string GuestIdentityId { get; set; } = "";

    /// <summary>The set (an <see cref="Event"/> id) this mark belongs to.</summary>
    public string EventId { get; set; } = "";

    /// <summary>The photo (a PhotoAsset id) marked.</summary>
    public string PhotoId { get; set; } = "";

    /// <summary>The studio (a TenantRecord id) the set belongs to.</summary>
    public string StudioTenantId { get; set; } = "";

    /// <summary>The guest's favorite flag (distinct from the studio's PhotoAsset.IsFavorite).</summary>
    public bool IsFavorite { get; set; }

    /// <summary>The guest's rating 0–5 (distinct from the studio's PhotoAsset.Rating).</summary>
    public int Rating { get; set; }

    /// <summary>The guest "I select this for delivery/print" pick — the core proofing signal the studio reads.</summary>
    public bool IsSelected { get; set; }

    /// <summary>An optional guest comment (only honored when the grant + collection allow comments).</summary>
    public string? Comment { get; set; }

    /// <summary>Updated on every save.</summary>
    [Timestamp(OnSave = true)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Deterministic id — one selection row per (guest, photo).</summary>
    public static string KeyFor(string guestIdentityId, string photoId) => DeterministicId.From(guestIdentityId, photoId);
}
