namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>The DID has no session this application can use — none, one that ended, or one that no longer matches its
/// identity, PDS, issuer or client — so the person signs in or grants access again before anything is sent as them.</summary>
public sealed class AtprotoAuthorizationRequiredException(string message, Exception? cause = null) : InvalidOperationException(message, cause);
