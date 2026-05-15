using Koan.Web.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Koan.Web.Extensions;

/// <summary>
/// Opt-in registration for <see cref="KoanCacheControlMiddleware"/>. Apps add the middleware
/// where they want it in the pipeline — typically early, before controllers, so the
/// <c>EntityContext.CacheBehavior</c> scope is in place by the time entity calls run.
/// </summary>
public static class CacheControlExtensions
{
    /// <summary>
    /// Insert the Koan cache-control middleware into the request pipeline. Reads
    /// <c>Cache-Control</c> (<c>no-cache</c>/<c>no-store</c>) and <c>X-Koan-Cache</c>
    /// (<c>refresh</c>/<c>bypass</c>/<c>readonly</c>) headers and pushes the corresponding
    /// <c>EntityContext.CacheBehavior</c> for the request scope.
    /// </summary>
    public static IApplicationBuilder UseKoanCacheControl(this IApplicationBuilder app)
        => app.UseMiddleware<KoanCacheControlMiddleware>();
}
