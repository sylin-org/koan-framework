namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// The application's base URL names no AT Protocol client, so a step that publishes the client identity cannot run. Its
/// message is the operator's correction and holds nothing from a provider, so it is safe to log.
/// </summary>
internal sealed class AtprotoClientIdentityException(string correction) : InvalidOperationException(correction);
