using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Web.Auth.Options;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// SEC-0001 §10 — single source of truth for resolving a post-authentication return URL and the
/// kind of redirect it warrants. The allow-list is the security boundary (WEB-0043): a local path of
/// this application, or an absolute URL whose prefix is allow-listed (the cross-origin case the
/// allow-list exists to support), is accepted; anything else falls back to the default landing, never
/// to the value.
/// <para>
/// SEC-0029: a local path starts with exactly one <c>/</c>, is printable ASCII without spaces, and after
/// percent-decoding its path names no host (<c>//</c>), carries no backslash and no <c>.</c> or <c>..</c>
/// segment, and lies under the application's path base. <c>//evil.example/x</c>, <c>/\evil.example</c> and
/// <c>/%2F%2Fevil.example</c> are not local paths: a browser reads each as another host.
/// </para>
/// <para>
/// It also reports whether the resolved URL is same-site, because the caller must pick the right
/// redirect verb: an allow-listed <em>absolute</em> URL has to use a plain redirect — a local-only
/// redirect (<c>Url.IsLocalUrl</c>) rejects any URL with a host/authority and would otherwise 500.
/// </para>
/// </summary>
public static class ReturnUrlPolicy
{
    private const int MaximumDecodingPasses = 3;

    public readonly record struct Resolution(string Url, bool IsLocal);

    /// <summary>
    /// The landing for <paramref name="candidate"/> in this request: the candidate when it is a local path under the
    /// application or an allow-listed absolute URL, else the default landing (<see cref="DefaultLanding"/>).
    /// </summary>
    public static Resolution Resolve(HttpContext context, string? candidate, ReturnUrlOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        return Resolve(candidate, options.AllowList, DefaultLanding(context, options.DefaultPath), ApplicationPaths.PathBase(context));
    }

    /// <summary>
    /// <paramref name="candidate"/> when it is a local path under <paramref name="pathBase"/> or an allow-listed
    /// absolute URL, else <paramref name="fallback"/>.
    /// </summary>
    public static Resolution Resolve(string? candidate, IReadOnlyList<string>? allowList, string fallback, PathString pathBase = default)
    {
        if (IsLocalPath(candidate, pathBase)) return new Resolution(candidate!, true);
        if (!string.IsNullOrWhiteSpace(candidate)
            && Uri.TryCreate(candidate, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            && allowList is not null
            && allowList.Any(p => candidate.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return new Resolution(candidate, false); // allow-listed absolute (cross-origin) URL
        return new Resolution(fallback, fallback.StartsWith('/') && !fallback.StartsWith("//", StringComparison.Ordinal));
    }

    /// <summary>
    /// SEC-0027 — where a person lands when no acceptable return is named: <paramref name="defaultPath"/> as an
    /// application-relative path under the application's path base, exactly once (<c>/</c> under <c>/notes</c> is
    /// <c>/notes/</c>), or as it is when it is an absolute http(s) URL.
    /// </summary>
    public static string DefaultLanding(HttpContext context, string? defaultPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(defaultPath)) return ApplicationPaths.Under(context, "/");
        if (Uri.TryCreate(defaultPath, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return defaultPath;
        return ApplicationPaths.Under(context, defaultPath.StartsWith('/') ? defaultPath : "/" + defaultPath);
    }

    /// <summary>
    /// SEC-0029 — true when <paramref name="candidate"/> is a local path of this application: one leading <c>/</c>,
    /// printable ASCII without spaces, a path that after percent-decoding names no host, holds no backslash and no
    /// <c>.</c> or <c>..</c> segment, and begins with <paramref name="pathBase"/> at a segment boundary.
    /// </summary>
    public static bool IsLocalPath(string? candidate, PathString pathBase = default)
    {
        if (string.IsNullOrEmpty(candidate) || candidate[0] != '/') return false;
        if (candidate.Length > 1 && candidate[1] is '/' or '\\') return false;
        if (candidate.Any(c => c is <= ' ' or > '~' or '\\')) return false;

        var end = candidate.IndexOfAny(['?', '#']);
        var path = end < 0 ? candidate : candidate[..end];
        var decoded = path;
        for (var pass = 0; pass < MaximumDecodingPasses && decoded.Contains('%'); pass++)
            decoded = Uri.UnescapeDataString(decoded);
        if (decoded.StartsWith("//", StringComparison.Ordinal)) return false;
        if (decoded.Any(c => c is < ' ' or '\\' or (char)0x7F)) return false;
        if (decoded.Split('/').Any(segment => segment is "." or "..")) return false;

        if (!pathBase.HasValue) return true;
        var prefix = pathBase.ToUriComponent();
        return path.StartsWith(prefix, StringComparison.Ordinal)
               && (path.Length == prefix.Length || path[prefix.Length] == '/');
    }

    /// <summary>
    /// WEB-0078: a rooted relative URL placed under <paramref name="pathBase"/>; an absolute URL, an empty one, a
    /// non-rooted one, or one that already begins with the exact path base (ordinal, at a segment boundary) is
    /// unchanged. Used for the self-hosted development provider's endpoints, which may already carry the base; a path
    /// Koan writes for the browser follows <see cref="ApplicationPaths.Under"/> instead (SEC-0027).
    /// </summary>
    public static string UnderPathBase(string url, PathString pathBase)
    {
        if (!pathBase.HasValue || string.IsNullOrEmpty(url) || !url.StartsWith('/')) return url;
        var prefix = pathBase.ToUriComponent();
        if (url.StartsWith(prefix, StringComparison.Ordinal) && (url.Length == prefix.Length || url[prefix.Length] is '/' or '?' or '#'))
            return url;
        return prefix + url;
    }
}
