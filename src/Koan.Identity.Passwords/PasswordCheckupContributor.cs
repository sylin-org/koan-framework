using Koan.Data.Core;
using Koan.Identity.Credentials;
using Koan.Identity.Credentials.Checkup;

namespace Koan.Identity.Passwords;

/// <summary>
/// SEC-0007 P3-grp4 — the password signal for the Security Checkup. Local password is OPTIONAL (D4), so this is a
/// green confirmation when a password is set and <b>silent when absent</b> — a passwordless / external-IdP user is
/// never nagged to add a password (passwordless-first stays delightful; the MFA contributor drives the "add a second
/// factor" nudge instead).
/// </summary>
internal sealed class PasswordCheckupContributor : ISecurityCheckupContributor
{
    public async Task<IReadOnlyList<CheckupSignal>> EvaluateAsync(string identityId, CancellationToken ct = default)
    {
        var hasPassword = await LocalCredential.Get(LocalCredential.KeyFor(identityId), ct).ConfigureAwait(false) is not null;
        return hasPassword
            ? new[] { new CheckupSignal("password", CheckupGrade.Green, "Password is set.") }
            : Array.Empty<CheckupSignal>();
    }
}

/// <summary>
/// SEC-0007 P3-grp4 — reports the local password as a Koan-owned PRIMARY factor, so the Security Checkup only nudges
/// "add two-factor authentication" for people whose primary sign-in Koan actually owns (not external-IdP-only users).
/// </summary>
internal sealed class PasswordPrimaryProbe : IPrimaryCredentialProbe
{
    public async Task<bool> HasPrimaryAsync(string identityId, CancellationToken ct = default)
        => await LocalCredential.Get(LocalCredential.KeyFor(identityId), ct).ConfigureAwait(false) is not null;
}
