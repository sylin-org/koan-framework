using System.Web;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// WEB-0078: the connector's public client identity derives from the application's canonical base URL — configured as
/// <c>Koan:Web:ApplicationUrl</c>, accepted by the application (WEB-0081), or the request's where Koan derives it — so
/// an application configures neither its client-metadata URL nor its redirect by hand. An explicit
/// <c>Koan:Web:Auth:Providers:atproto:ClientId</c> still overrides it.
/// </summary>
internal static class AtprotoClientIdentity
{
    internal const string BaseUrlKey = "Koan:Web:ApplicationUrl";

    /// <summary>
    /// The client-metadata URL for <paramref name="baseUrl"/>, or null when there is no base URL. An https base
    /// yields <c>{base}/auth/atproto/client-metadata.json</c>. A loopback http base yields the AT Protocol
    /// localhost development profile, whose <c>redirect_uri</c> is the IP-loopback callback under the base path;
    /// that profile exists only in Development, so outside it an http base yields null.
    /// </summary>
    public static string? Derive(string? baseUrl, IReadOnlyList<string> scopes, bool development)
    {
        if (baseUrl is null || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var configured)) return null;
        // The URL form keeps the path percent-encoded; PathBase is the decoded form the pipeline matches.
        var path = configured.AbsolutePath.TrimEnd('/');

        if (configured.Scheme == Uri.UriSchemeHttps)
            return configured.GetLeftPart(UriPartial.Authority) + path + Constants.Metadata;

        if (!development || configured.Scheme != Uri.UriSchemeHttp || !configured.IsLoopback) return null;

        var port = configured.IsDefaultPort ? string.Empty : ":" + configured.Port;
        var redirect = $"http://127.0.0.1{port}{path}{Constants.Callback}";
        return "http://localhost?redirect_uri=" + Uri.EscapeDataString(redirect) + "&scope=" + Uri.EscapeDataString(string.Join(' ', scopes));
    }

    /// <summary>
    /// The correction when no client identity can be derived: from no base URL at all, or from one that cannot name an AT
    /// Protocol client (an http URL outside Development's loopback profile). <paramref name="acceptable"/> says whether the
    /// application could accept a URL instead (it keeps an accepted URL and nothing is configured).
    /// </summary>
    public static string Correction(string? baseUrl, bool acceptable)
        => (baseUrl is null
               ? "The AT Protocol client identity derives from the application's base URL, and there is none. "
               : $"The application's base URL {baseUrl} cannot name an AT Protocol client: its client-metadata URL must be https, or loopback http in Development. ") +
           $"Set {BaseUrlKey} to this application's public https URL" +
           (acceptable ? ", have the application accept it," : "") +
           $" or set {Constants.ProviderSection}:ClientId to its absolute client-metadata URL.";

    /// <summary>
    /// The redirect URI the client identity implies: for an https metadata URL, the callback under the same
    /// authority and path prefix; for the localhost profile, its declared <c>redirect_uri</c>.
    /// </summary>
    public static string RedirectUri(string clientId)
    {
        var uri = new Uri(clientId);
        if (uri.Scheme != Uri.UriSchemeHttps)
            return HttpUtility.ParseQueryString(uri.Query)["redirect_uri"]!;

        var prefix = uri.AbsolutePath.EndsWith(Constants.Metadata, StringComparison.Ordinal)
            ? uri.AbsolutePath[..^Constants.Metadata.Length]
            : string.Empty;
        return uri.GetLeftPart(UriPartial.Authority) + prefix + Constants.Callback;
    }
}
