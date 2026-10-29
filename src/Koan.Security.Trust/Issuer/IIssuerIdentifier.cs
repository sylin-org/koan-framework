namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0023 — states the issuer identifier (<c>iss</c>) of the tokens this application issues, when an owner above
/// Trust publishes one. The embedded authorization server registers it, so the issuer its discovery metadata states
/// is the issuer every token carries and the one <c>Koan.bearer</c> accepts. With none registered, or when it states
/// none, the issuer is <c>Koan:Security:Trust:Issuer</c>, else <c>koan-dev</c>, which the bearer scheme always accepts.
/// </summary>
public interface IIssuerIdentifier
{
    /// <summary>
    /// The issuer identifier for the current request, or null when none can be stated — outside a request while the
    /// identifier derives from the request, or where deriving it is refused.
    /// </summary>
    string? Resolve();
}
