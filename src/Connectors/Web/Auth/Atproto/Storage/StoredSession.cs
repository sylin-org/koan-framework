using CarpaNet.OAuth.Storage;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>A session row as read, and its grant when the row holds a live one this application can read.</summary>
internal sealed record StoredSession(AtprotoSession Row, OAuthSessionData? Grant)
{
    /// <summary>Whether another process holds the grant to refresh or revoke it now.</summary>
    public bool Claimed => Row.ClaimedUntil is { } until && until > DateTimeOffset.UtcNow;
}
