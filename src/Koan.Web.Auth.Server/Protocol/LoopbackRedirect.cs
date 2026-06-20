using System.Net;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D5 — a dynamically-registered client may register only <b>loopback</b> redirect URIs (RFC 8252):
/// an IP loopback literal (<c>127.0.0.0/8</c> or <c>::1</c>, any port) or <c>localhost</c>. This is the
/// zero-trust constraint that stops an open registration from minting a client that exfiltrates codes to an
/// attacker-controlled host.
/// </summary>
internal static class LoopbackRedirect
{
    public static bool IsLoopback(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (IPAddress.TryParse(uri.Host, out var ip)) return IPAddress.IsLoopback(ip);
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
