using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 D5 — a verified <b>email factor</b> attached to a person. One <see cref="Identity"/> holds many; the
/// person-not-email model (multiple attached factors) dissolves duplicate accounts and departure-lockout at the
/// root. The normalized <see cref="Address"/> is also the join key for safe domain routing (P4).
/// </summary>
public sealed class IdentityEmail : Entity<IdentityEmail>, IAmbientExempt
{
    /// <summary>The owning person (<see cref="Identity"/> id).</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>The normalized (lower-cased, trimmed) email address.</summary>
    public string Address { get; set; } = "";

    /// <summary>True when the address has been verified (IdP-asserted or via a verification flow).</summary>
    public bool Verified { get; set; }

    /// <summary>True for the person's primary address (the first verified factor by default).</summary>
    public bool Primary { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The deterministic id for an (identityId, normalized address) pair, so concurrent first sign-ins of the same
    /// person upsert one factor row instead of racing two duplicates (with two <c>Primary=true</c>).
    /// </summary>
    public static string KeyFor(string identityId, string normalizedAddress)
        => DeterministicId.From(identityId, normalizedAddress);
}
