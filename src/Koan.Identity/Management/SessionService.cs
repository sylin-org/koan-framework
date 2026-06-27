using Koan.Data.Core;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0007 Layer 1 — durable session / device management, including the "sign out everywhere-else" power verb.
/// </summary>
public sealed class SessionService
{
    /// <summary>Record a new device session for a person.</summary>
    public async Task<Session> RecordAsync(string identityId, string? device, string? browser, string? os, string? approxCity, CancellationToken ct = default)
    {
        var session = new Session { IdentityId = identityId, Device = device, Browser = browser, Os = os, ApproxCity = approxCity };
        return await session.Save(ct).ConfigureAwait(false);
    }

    /// <summary>List a person's sessions (the device list).</summary>
    public Task<IReadOnlyList<Session>> ListAsync(string identityId, CancellationToken ct = default)
        => Session.Query(s => s.IdentityId == identityId, ct);

    /// <summary>Revoke a single session. Returns false if it was missing or already revoked.</summary>
    public async Task<bool> RevokeAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await Session.Get(sessionId, ct).ConfigureAwait(false);
        if (session is null || session.Revoked) return false;
        session.Revoked = true;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await session.Save(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Revoke every active session for a person except <paramref name="currentSessionId"/>. Enforcement is on the
    /// request path: <see cref="SessionGuard"/> rejects a cookie whose session is revoked at the next validation
    /// tick, so the effect is immediate + observable (the device list shows them revoked). Returns the count revoked.
    /// </summary>
    public Task<int> SignOutEverywhereElseAsync(string identityId, string currentSessionId, CancellationToken ct = default)
        => RevokeManyAsync(identityId, currentSessionId, ct);

    /// <summary>
    /// Revoke <b>every</b> active session for a person (no exception) — the session leg of atomic deprovisioning
    /// (SEC-0007 P4). Enforcement is the same request-path <see cref="SessionGuard"/> as everywhere-else, so a
    /// deactivated person's existing cookies stop working at the next validation tick. Returns the count revoked.
    /// </summary>
    public Task<int> RevokeAllAsync(string identityId, CancellationToken ct = default)
        => RevokeManyAsync(identityId, currentSessionId: null, ct);

    private static async Task<int> RevokeManyAsync(string identityId, string? currentSessionId, CancellationToken ct)
    {
        var sessions = await Session.Query(s => s.IdentityId == identityId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var revoked = 0;
        foreach (var s in sessions)
        {
            if (s.Id == currentSessionId || s.Revoked) continue;
            s.Revoked = true;
            s.RevokedAt = now;
            await s.Save(ct).ConfigureAwait(false);
            revoked++;
        }

        return revoked;
    }
}
