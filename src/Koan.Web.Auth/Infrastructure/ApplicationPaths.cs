using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// The one rule for a path Koan writes for the browser (SEC-0027): an application-relative, rooted path — one of Koan's
/// own routes, or a page path the application configured — gets the application's path base exactly once. The path
/// base is the configured base URL's (WEB-0078), canonical as configured, else the request's. A path that happens to
/// begin with the same segment as the path base is still prefixed: <c>/oauth/request</c> under <c>/oauth</c> is
/// <c>/oauth/oauth/request</c>.
/// </summary>
public static class ApplicationPaths
{
    /// <summary>The application's path base: the configured base URL's path, else the request's.</summary>
    public static PathString PathBase(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.RequestServices?.GetService<KoanWebBaseUrl>() is { IsConfigured: true } baseUrl
            ? baseUrl.PathBase
            : context.Request.PathBase;
    }

    /// <summary>The application-relative, rooted <paramref name="path"/> under the path base, exactly once; the path may
    /// carry a query.</summary>
    public static string Under(HttpContext context, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return PathBase(context).ToUriComponent() + path;
    }
}
