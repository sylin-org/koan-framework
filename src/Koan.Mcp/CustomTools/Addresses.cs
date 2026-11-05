using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// An address of this application, as the edge takes one (AI-0042): a path under the base URL, or an absolute URL whose origin is the
/// base URL's in force or the one the call arrived on, under its path. What it names is a path and query under the base URL, its dot
/// segments resolved as HTTP resolves them and its fragment dropped, so it can be read in process (ARCH-0144). Any other host is
/// someone else's, and is refused.
/// </summary>
internal static class Addresses
{
    /// <summary>Takes <paramref name="address"/> as a path and query under the base URL in <paramref name="local"/>, or says in
    /// <paramref name="refusal"/> why it is no address of this application.</summary>
    public static bool TryLocal(string address, HttpContext? inHand, KoanWebBaseUrl baseUrl, out string local, out string refusal)
    {
        local = "/";
        refusal = "";
        var given = address.Trim();
        var roots = Roots(inHand, baseUrl);
        Uri? absolute = null;
        if (given.StartsWith("//", StringComparison.Ordinal))
            Uri.TryCreate((roots.FirstOrDefault()?.Scheme ?? Uri.UriSchemeHttps) + ":" + given, UriKind.Absolute, out absolute);
        else if (!given.StartsWith('/') && Uri.TryCreate(given, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            absolute = parsed;

        if (absolute is null)
        {
            // A path is under the base URL already; one without its slash is the same path.
            if (given.StartsWith("//", StringComparison.Ordinal) || !Uri.TryCreate(new Uri("http://local"), "/" + given.TrimStart('/'), out var path))
            {
                refusal = $"'{address}' is not an address.";
                return false;
            }

            local = path.PathAndQuery;
            return true;
        }

        foreach (var root in roots)
        {
            if (!string.Equals(root.Scheme, absolute.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(root.Host, absolute.Host, StringComparison.OrdinalIgnoreCase) || root.Port != absolute.Port)
                continue;
            var under = root.AbsolutePath.TrimEnd('/');
            var path = absolute.AbsolutePath;
            if (under.Length > 0 && !(path.Equals(under, StringComparison.OrdinalIgnoreCase) || path.StartsWith(under + "/", StringComparison.OrdinalIgnoreCase)))
                continue;
            local = (path.Length > under.Length ? path[under.Length..] : "/") + absolute.Query;
            return true;
        }

        refusal = roots.Count == 0
            ? $"'{address}' is an absolute address, and this application does not know its own yet, so give its path."
            : $"'{address}' is an address of {absolute.Authority}; only addresses of {roots[0].Authority} are read here.";
        return false;
    }

    /// <summary>The application's address as absolute <paramref name="local"/>: under the base URL in force, else the one the call
    /// arrived on, else the path alone.</summary>
    public static string Absolute(string local, HttpContext? inHand, KoanWebBaseUrl baseUrl)
        => Roots(inHand, baseUrl).FirstOrDefault() is { } root ? root.GetLeftPart(UriPartial.Authority) + root.AbsolutePath.TrimEnd('/') + local : local;

    /// <summary>Where this application answers: the base URL in force, and the origin and path base the request in hand arrived on.</summary>
    private static IReadOnlyList<Uri> Roots(HttpContext? inHand, KoanWebBaseUrl baseUrl)
    {
        var roots = new List<Uri>(2);
        if (baseUrl.Current is { } current && Uri.TryCreate(current, UriKind.Absolute, out var configured)) roots.Add(configured);
        if (inHand is not null && inHand.Request.Host.HasValue
            && Uri.TryCreate($"{inHand.Request.Scheme}://{inHand.Request.Host}{inHand.Request.PathBase}", UriKind.Absolute, out var arrived))
            roots.Add(arrived);
        return roots;
    }
}
