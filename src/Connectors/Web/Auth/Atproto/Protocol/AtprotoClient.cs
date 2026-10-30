using CarpaNet.OAuth;
using Koan.Web.Auth.Connector.Atproto.Storage;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// A restored or signed-in SDK client for one DID: the client identity it was built for (WEB-0081), the PDS and issuer
/// it was verified against, and the stored grant whose tokens it holds in memory. It is reused only while the session
/// row still holds that grant, so a refresh another process stored is never answered with tokens it replaced.
/// </summary>
internal sealed class AtprotoClient(string clientId, string grant, string pds, string issuer,
    OAuthSession oauth, ATProtoOAuthClient client, SessionCapture capture) : IDisposable
{
    private string grant = grant;

    public string ClientId { get; } = clientId;

    /// <summary>The protected grant, as stored, whose tokens this client holds.</summary>
    public string Grant
    {
        get => Volatile.Read(ref grant);
        set => Volatile.Write(ref grant, value);
    }

    public string Pds { get; } = pds;
    public string Issuer { get; } = issuer;
    public Uri BaseUrl => client.BaseUrl;
    public DPoPTokenProvider Tokens => (DPoPTokenProvider)client.TokenProvider;

    /// <summary>Where the SDK writes the grant a refresh returns, for the connector to store under its claim.</summary>
    public SessionCapture Capture { get; } = capture;

    public void Dispose()
    {
        client.Dispose();
        oauth.Dispose();
    }
}
