using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity.Mfa;

/// <summary>The second-factor type. TOTP is Phase 1; passkey-as-2FA arrives with Koan.Identity.Passkeys (Phase 2).</summary>
public enum MfaType
{
    /// <summary>RFC 6238 time-based one-time password (authenticator app).</summary>
    Totp = 0,
}

/// <summary>
/// SEC-0007 P3-grp4 (D4 opt-in) — a person's enrolled second factor. The TOTP shared secret is a recoverable secret
/// (unlike a password, it must be decryptable to compute the expected code), so it is stored <b>encrypted at rest</b>
/// (<see cref="Secret"/> holds the protected value — never plaintext). An enrollment does not gate sign-in until
/// <see cref="ConfirmedAt"/> is set (the user proved a first code). <see cref="LastStepUsed"/> is the anti-replay
/// watermark. <c>IAmbientExempt</c> (global plane). One TOTP enrollment per person (deterministic id).
/// </summary>
public sealed class MfaEnrollment : Entity<MfaEnrollment>, IAmbientExempt
{
    /// <summary>The owning person.</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>The factor type.</summary>
    public MfaType Type { get; set; } = MfaType.Totp;

    /// <summary>The PROTECTED (encrypted-at-rest) TOTP shared secret — never plaintext on disk.</summary>
    public string Secret { get; set; } = "";

    /// <summary>A friendly label (e.g. the account/app name shown in the authenticator).</summary>
    public string? Label { get; set; }

    /// <summary>Set when the user proved a first code; an unconfirmed enrollment does not gate sign-in.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>The last accepted TOTP time-step — a code at or below this is rejected (anti-replay within the window).</summary>
    public long LastStepUsed { get; set; }

    /// <summary>Consecutive failed verify attempts since the last success — drives the brute-force lockout.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>When set and in the future, the factor is locked (too many failures) and rejects without checking the code.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True once the enrollment has been confirmed (and therefore gates sign-in).</summary>
    public bool IsConfirmed => ConfirmedAt is not null;

    /// <summary>True when the factor is currently locked out for brute-force protection.</summary>
    public bool IsLockedAt(DateTimeOffset now) => LockedUntil is { } until && now < until;

    /// <summary>One enrollment per (person, type) — re-enrolling upserts the one row.</summary>
    public static string KeyFor(string identityId, MfaType type) => DeterministicId.From("mfa", identityId, type.ToString());
}
