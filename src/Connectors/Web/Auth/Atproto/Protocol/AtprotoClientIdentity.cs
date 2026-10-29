using System.Web;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Hosting;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// WEB-0078: the connector's public client identity derives from the application's canonical base URL
/// (<c>Koan:Web:ApplicationUrl</c>), so an application configures neither its client-metadata URL nor its
/// redirect by hand. An explicit <c>Koan:Web:Auth:Providers:atproto:ClientId</c> still overrides it.
/// </summary>
internal static class AtprotoClientIdentity
{
    internal const string BaseUrlKey = "Koan:Web:ApplicationUrl";

    /// <summary>
    /// The client-metadata URL for the configured base URL, or null when no base URL is configured. An https base
    /// yields <c>{base}/auth/atproto/client-metadata.json</c>. A loopback http base yields the AT Protocol
    /// localhost development profile, whose <c>redirect_uri</c> is the IP-loopback callback under the base path;
    /// that profile exists only in Development, so outside it an http base yields null.
    /// </summary>
    public static string? Derive(KoanWebBaseUrl baseUrl, IReadOnlyList<string> scopes, bool development)
    {
        if (baseUrl?.Configured is null || !Uri.TryCreate(baseUrl.Configured, UriKind.Absolute, out var configured)) return null;
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
