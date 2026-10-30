using CarpaNet.OAuth.Storage;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>
/// One process's claim on a session row, to refresh or revoke its grant: the row as the claim wrote it. The holder makes
/// its request and settles the claim with one write: the result, while the row holds the grant it refreshed from, or the
/// end, while the row is as the claim wrote it. Disposing a claim that is not settled releases it, on the same condition.
/// </summary>
internal sealed class SessionClaim(AtprotoSessionStore store, AtprotoSession claimed) : IAsyncDisposable
{
    private bool settled;

    /// <summary>
    /// Stores the grant a refresh returned and clears the claim in the same write, whichever claim holds the row then. Null
    /// when the row holds another grant, so the caller reads it again. A store that fails the write fails the request, with
    /// a <see cref="TimeoutException"/> to try again.
    /// </summary>
    public async Task<StoredSession?> Refreshed(OAuthSessionData grant)
    {
        var next = store.Granted(claimed.Id, claimed.Did, grant);
        bool stored;
        try { stored = await store.WriteRefreshed(next, claimed.Grant!); }
        catch (Exception error)
        {
            store.LogUnstored(claimed.Id, error);
            throw new TimeoutException("The refreshed AT session could not be stored; try again.", error);
        }

        settled = true;
        return stored ? new StoredSession(next, grant) : null;
    }

    /// <summary>
    /// Ends the claimed session: the result of a revocation, or of a provider that ended the grant. False when the row is no
    /// longer as the claim wrote it — it was signed in again, refreshed, or claimed anew — so this claim ended nothing.
    /// </summary>
    public async Task<bool> End()
    {
        var ended = await store.End(claimed);
        settled = true;
        return ended;
    }

    public async ValueTask DisposeAsync()
    {
        if (settled) return;
        settled = true;
        try { await store.Release(claimed); }
        catch (Exception)
        {
            // The claim stands until its lease ends.
        }
    }
}
