using System;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Middleware;

/// <summary>
/// Maps HTTP cache-control intent onto <c>EntityContext.WithCacheBehavior</c> for the
/// duration of the request. Standard <c>Cache-Control</c> semantics are honoured, and a
/// framework-specific <c>X-Koan-Cache</c> header offers finer control.
/// </summary>
/// <remarks>
/// <para>
/// Header mapping:
/// <list type="bullet">
///   <item><c>Cache-Control: no-cache</c> → <see cref="CacheBehavior.Refresh"/> (skip cache, hit DB, repopulate)</item>
///   <item><c>Cache-Control: no-store</c> → <see cref="CacheBehavior.Bypass"/> (skip cache, hit DB, no populate)</item>
///   <item><c>X-Koan-Cache: refresh|bypass|readonly|default</c> overrides any <c>Cache-Control</c></item>
/// </list>
/// </para>
/// <para>
/// Opt-in via <c>app.UseKoanCacheControl()</c>. The scope is disposed in a finally block so
/// no AsyncLocal leak occurs even if downstream middleware throws.
/// </para>
/// </remarks>
public sealed class KoanCacheControlMiddleware
{
    private const string KoanCacheHeader = "X-Koan-Cache";
    private const string CacheControlHeader = "Cache-Control";

    private readonly RequestDelegate _next;
    private readonly ILogger<KoanCacheControlMiddleware> _logger;

    public KoanCacheControlMiddleware(RequestDelegate next, ILogger<KoanCacheControlMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var behavior = ResolveBehavior(context);
        if (behavior is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        using var _ = EntityContext.WithCacheBehavior(behavior.Value);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Koan.Cache: request scope behavior={Behavior} path={Path}", behavior, context.Request.Path);
        }

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the cache behavior to push for this request, or null when neither header
    /// indicates an override. <c>X-Koan-Cache</c> wins over <c>Cache-Control</c>.
    /// </summary>
    private static CacheBehavior? ResolveBehavior(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(KoanCacheHeader, out var koanValues))
        {
            var explicitValue = koanValues.ToString();
            return ParseKoanHeader(explicitValue);
        }

        if (context.Request.Headers.TryGetValue(CacheControlHeader, out var cacheControlValues))
        {
            var cacheControl = cacheControlValues.ToString();
            if (ContainsDirective(cacheControl, "no-cache")) return CacheBehavior.Refresh;
            if (ContainsDirective(cacheControl, "no-store")) return CacheBehavior.Bypass;
        }

        return null;
    }

    private static CacheBehavior? ParseKoanHeader(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "refresh" => CacheBehavior.Refresh,
            "bypass" or "no-cache" or "no-store" => CacheBehavior.Bypass,
            "readonly" or "read-only" => CacheBehavior.ReadOnly,
            "default" => CacheBehavior.Default,
            _ => null
        };
    }

    private static bool ContainsDirective(string headerValue, string directive)
    {
        // Cache-Control values are comma-separated directives. Match the token loosely
        // (case-insensitive, surrounding whitespace tolerant).
        if (string.IsNullOrEmpty(headerValue)) return false;

        var parts = headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Equals(directive, StringComparison.OrdinalIgnoreCase)) return true;
            // Directives may carry parameters (e.g., max-age=60); match prefix-then-equals.
            var equalsIdx = part.IndexOf('=');
            if (equalsIdx > 0 && part[..equalsIdx].Trim().Equals(directive, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
