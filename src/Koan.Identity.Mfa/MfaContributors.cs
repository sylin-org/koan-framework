using Koan.Data.Core;
using Koan.Identity.Credentials;
using Koan.Identity.Credentials.Checkup;
using Koan.Identity.Credentials.StepUp;

namespace Koan.Identity.Mfa;

/// <summary>
/// SEC-0007 P3-grp4 — the MFA step-up requirement: once a person has a CONFIRMED authenticator, the 2-phase gate
/// requires a second factor (<c>otp</c> now, <c>passkey</c> in Phase 2) at sign-in. An unconfirmed enrollment imposes
/// nothing (so enrolling can't lock you out mid-setup). Discovered + composed by the generic step-up gate.
/// </summary>
internal sealed class MfaStepUpRequirementContributor : IStepUpRequirementContributor
{
    public async Task<IReadOnlyList<StepUpRequirement>> RequiredForAsync(string identityId, CancellationToken ct = default)
    {
        var enrollment = await MfaEnrollment.Get(MfaEnrollment.KeyFor(identityId, MfaType.Totp), ct).ConfigureAwait(false);
        if (enrollment?.IsConfirmed != true) return Array.Empty<StepUpRequirement>();
        // A redeemed recovery code (amr=recovery) ALSO satisfies the requirement — so a lost authenticator does not
        // lock the person out; this is what makes the recovery-codes Checkup nudge an honest safety net.
        return new[] { new StepUpRequirement("mfa", new HashSet<string>(StringComparer.Ordinal) { CredentialAuthClaims.Totp, CredentialAuthClaims.Passkey, CredentialAuthClaims.Recovery }) };
    }
}

/// <summary>
/// SEC-0007 P3-grp4 — the MFA Security Checkup signal: green when a second factor is on, <b>amber with a "do this
/// next" nudge</b> when it is not — but only for a person whose PRIMARY factor Koan owns (a local password). An
/// external-IdP-only person relies on their IdP's MFA, so a Koan-side "add 2FA" nag would be a false alarm; for them
/// the signal stays silent. This keeps the Checkup honest (the delight — synthesis as substrate, never a false nudge).
/// </summary>
internal sealed class MfaCheckupContributor : ISecurityCheckupContributor
{
    private readonly IEnumerable<IPrimaryCredentialProbe> _primaryProbes;

    public MfaCheckupContributor(IEnumerable<IPrimaryCredentialProbe> primaryProbes) => _primaryProbes = primaryProbes;

    public async Task<IReadOnlyList<CheckupSignal>> EvaluateAsync(string identityId, CancellationToken ct = default)
    {
        var enrollment = await MfaEnrollment.Get(MfaEnrollment.KeyFor(identityId, MfaType.Totp), ct).ConfigureAwait(false);
        if (enrollment?.IsConfirmed == true)
            return new[] { new CheckupSignal("mfa", CheckupGrade.Green, "Two-factor authentication is on.") };

        // No second factor — only nudge when Koan owns the primary factor (else the IdP handles MFA; nagging is dishonest).
        var ownsPrimary = false;
        foreach (var probe in _primaryProbes)
            if (await probe.HasPrimaryAsync(identityId, ct).ConfigureAwait(false)) { ownsPrimary = true; break; }

        return ownsPrimary
            ? new[] { new CheckupSignal("mfa", CheckupGrade.Amber, "Add two-factor authentication for stronger protection.", "Add 2FA") }
            : Array.Empty<CheckupSignal>();
    }
}

/// <summary>
/// SEC-0007 P3-grp4 — the recovery-codes Checkup signal, surfaced only once a second factor exists (recovery is the
/// safety net for an MFA-device loss). Amber "set this up before you're locked out" until codes are provisioned —
/// pre-provisioned recovery framed as care, not an after-the-fact scramble.
/// </summary>
internal sealed class RecoveryCheckupContributor : ISecurityCheckupContributor
{
    public async Task<IReadOnlyList<CheckupSignal>> EvaluateAsync(string identityId, CancellationToken ct = default)
    {
        var enrollment = await MfaEnrollment.Get(MfaEnrollment.KeyFor(identityId, MfaType.Totp), ct).ConfigureAwait(false);
        if (enrollment?.IsConfirmed != true) return Array.Empty<CheckupSignal>(); // no MFA yet → the MFA nudge leads

        var remaining = (await RecoveryCode.Query(c => c.IdentityId == identityId && c.UsedAt == null, ct).ConfigureAwait(false)).Count;
        return remaining > 0
            ? new[] { new CheckupSignal("recovery", CheckupGrade.Green, $"{remaining} recovery codes remaining.") }
            : new[] { new CheckupSignal("recovery", CheckupGrade.Amber, "Set up recovery codes before you're locked out.", "Set up recovery") };
    }
}
