using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Koan.Web.OpenGraph;

/// <summary>
/// Matches a request path against one registered route template and extracts the primary token.
/// The primary token is the first route parameter; everything after it (further segments, a
/// decorative SEO slug) is matched and discarded. So <c>/work/{id}</c>, <c>/work/{id}/{slug}</c>,
/// and a request like <c>/work/{id}/anything</c> all resolve off the same primary token.
/// </summary>
internal sealed class RouteTokenMatcher
{
    private readonly TemplateMatcher _exact;
    private readonly TemplateMatcher _withTrailing;
    private readonly string _tokenName;

    public RouteTokenMatcher(string routeTemplate)
    {
        if (string.IsNullOrWhiteSpace(routeTemplate))
        {
            throw new ArgumentException("Route template must be provided.", nameof(routeTemplate));
        }

        var segments = routeTemplate.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var primaryIndex = Array.FindIndex(segments, s => s.Contains('{'));
        if (primaryIndex < 0)
        {
            throw new ArgumentException(
                $"Route template '{routeTemplate}' must declare at least one parameter (the primary token), e.g. \"/work/{{id}}\".",
                nameof(routeTemplate));
        }

        _tokenName = ExtractParameterName(segments[primaryIndex]);

        // Truncate at the primary token so any trailing segments the template declares (a slug) are
        // ignored, then build a sibling matcher that tolerates trailing segments the request adds.
        var prefix = "/" + string.Join('/', segments[..(primaryIndex + 1)]);
        _exact = new TemplateMatcher(TemplateParser.Parse(prefix), new RouteValueDictionary());
        _withTrailing = new TemplateMatcher(TemplateParser.Parse(prefix + "/{**__og_rest}"), new RouteValueDictionary());
    }

    public bool TryExtractToken(string path, out string token)
    {
        token = string.Empty;
        var pathString = new PathString(NormalizePath(path));

        if (TryMatch(_exact, pathString, out var value) || TryMatch(_withTrailing, pathString, out value))
        {
            token = value;
            return !string.IsNullOrEmpty(token);
        }

        return false;
    }

    private bool TryMatch(TemplateMatcher matcher, PathString path, out string token)
    {
        token = string.Empty;
        var values = new RouteValueDictionary();
        if (!matcher.TryMatch(path, values))
        {
            return false;
        }

        if (values.TryGetValue(_tokenName, out var raw) && raw is not null)
        {
            token = raw.ToString() ?? string.Empty;
            return true;
        }

        return false;
    }

    private static string ExtractParameterName(string segment)
    {
        // "{id}" -> "id"; "{**slug}" -> "slug"; "{id:int}" / "{id?}" -> "id".
        var name = segment.Trim('{', '}').TrimStart('*');
        var cut = name.IndexOfAny(new[] { ':', '?', '=' });
        return cut >= 0 ? name[..cut] : name;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        return path[0] == '/' ? path : "/" + path;
    }
}
