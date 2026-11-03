using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Hosting;

/// <summary>
/// SEC-0039 — whether an unsafe request that carries a cookie came from a page of another site. A forgery borrows an
/// ambient credential, so a request with no cookie has nothing to borrow and is not judged. <c>Sec-Fetch-Site</c> answers
/// when the browser sends it, and only <c>same-origin</c> and <c>none</c> (the person typing an address) pass; else an
/// <c>Origin</c> must be this request's own, <c>null</c> included as not. A request with neither header is not a browser's.
/// No Koan sign-in posts across sites: its OIDC, OAuth and atproto callbacks are redirects (<c>ResponseMode=query</c>).
/// </summary>
internal static class CrossSiteWrites
{
    private const string FetchSite = "Sec-Fetch-Site";

    public static bool Refused(HttpRequest request)
    {
        var method = request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)) return false;
        if (!request.Headers.ContainsKey(HeaderNames.Cookie)) return false;

        var site = request.Headers[FetchSite].ToString();
        if (site.Length > 0) return site is not ("same-origin" or "none");

        var origin = request.Headers.Origin.ToString();
        return origin.Length > 0 && !string.Equals(origin, $"{request.Scheme}://{request.Host.Value}", StringComparison.OrdinalIgnoreCase);
    }

    public static Task Refuse(HttpContext context)
        => Results.Problem(
            "A page of another site asked for this, and it is not done for one. Ask from this application's own pages.",
            statusCode: StatusCodes.Status403Forbidden,
            title: "Not from here",
            extensions: new Dictionary<string, object?> { ["kind"] = "gated" }).ExecuteAsync(context);
}
