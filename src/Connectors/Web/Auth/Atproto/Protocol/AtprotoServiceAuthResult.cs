namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// The answer to a service-auth token. <see cref="Did"/> is the issuer it proves, and <see cref="Handle"/> the handle that
/// DID's document names when that handle resolves back to it; both only when <see cref="Verified"/>.
/// </summary>
public sealed record AtprotoServiceAuthResult(AtprotoServiceAuthOutcome Outcome, string? Did = null, string? Handle = null)
{
    /// <summary>Whether the token proves its issuer.</summary>
    public bool Verified => Outcome == AtprotoServiceAuthOutcome.Verified;
}
