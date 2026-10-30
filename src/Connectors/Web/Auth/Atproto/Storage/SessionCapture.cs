using CarpaNet.OAuth.Storage;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>
/// The SDK's session store for one client, which persists nothing: the connector stores a grant only through
/// <see cref="AtprotoSessionStore"/>, conditionally. A sign-in's first write is verified, then held until the browser,
/// DID, PDS and issuer checks pass; a refresh's write is held until the connector stores it under its claim. Restoring
/// reads the grant the client was built from.
/// </summary>
internal sealed class SessionCapture(OAuthSessionData? restored = null,
    Func<OAuthSessionData, CancellationToken, Task>? verifyFirst = null) : IOAuthSessionStore
{
    private readonly object gate = new();
    private OAuthSessionData? current = restored;
    private OAuthSessionData? written;
    private string? subject = restored?.TokenSet.Sub;
    private bool verified = verifyFirst is null;

    /// <summary>The grant the SDK wrote last, if it wrote one since it was last taken.</summary>
    public OAuthSessionData? Written
    {
        get { lock (gate) return written; }
    }

    /// <summary>Takes the grant the SDK wrote last, if it wrote one since it was last taken.</summary>
    public OAuthSessionData? Take()
    {
        lock (gate)
        {
            var taken = written;
            written = null;
            return taken;
        }
    }

    public async Task StoreAsync(string sub, OAuthSessionData value, CancellationToken cancellationToken = default)
    {
        lock (gate)
            if (sub != value.TokenSet.Sub || (subject is not null && sub != subject)) throw new InvalidOperationException("AT token subject changed.");
        if (!verified)
        {
            await verifyFirst!(value, cancellationToken);
            verified = true;
        }

        lock (gate) { subject = sub; current = written = value; }
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(sub == subject ? current : null);
    }

    /// <summary>Nothing to delete: the connector ends a session through <see cref="AtprotoSessionStore.End"/>.</summary>
    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
