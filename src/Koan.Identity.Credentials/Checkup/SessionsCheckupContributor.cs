using Koan.Data.Core;

namespace Koan.Identity.Credentials.Checkup;

/// <summary>
/// SEC-0007 P3-grp4 — the always-present "active devices" Checkup signal over the durable <see cref="Session"/> list
/// (cheap because Koan already owns the device twin + revocation). Informational/green — a device list is a review
/// prompt, not a weakness; the factor packages add the amber nudges (add MFA, set up recovery).
/// </summary>
internal sealed class SessionsCheckupContributor : ISecurityCheckupContributor
{
    public async Task<IReadOnlyList<CheckupSignal>> EvaluateAsync(string identityId, CancellationToken ct = default)
    {
        var active = await Session.Query(s => s.IdentityId == identityId && !s.Revoked, ct).ConfigureAwait(false);
        var n = active.Count;
        var message = n switch { 0 => "No active devices.", 1 => "1 active device.", _ => $"{n} active devices." };
        return new[] { new CheckupSignal("devices", CheckupGrade.Green, message, n > 1 ? "Review your devices" : null) };
    }
}
