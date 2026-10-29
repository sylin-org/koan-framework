using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// SEC-0001 §10 — single source of truth for resolving a post-authentication return URL and the
/// kind of redirect it warrants. The allow-list is the security boundary (WEB-0043): a same-site
/// relative path, or an absolute URL whose prefix is allow-listed (the cross-origin case the
/// allow-list exists to support), is accepted; anything else falls back to a safe default.
/// <para>
/// It also reports whether the resolved URL is same-site, because the caller must pick the right
/// redirect verb: an allow-listed <em>absolute</em> URL has to use a plain redirect — a local-only
/// redirect (<c>Url.IsLocalUrl</c>) rejects any URL with a host/authority and would otherwise 500.
/// This is the defect SEC-0001 §10 fixes: challenge-time validation accepted the absolute URL, but
/// the callback then handed it to <c>LocalRedirect</c>, which rejected it.
/// </para>
/// </summary>
public static class ReturnUrlPolicy
{
    public readonly record struct Resolution(string Url, bool IsLocal);

    public static Resolution Resolve(string? candidate, IReadOnlyList<string>? allowList, string fallback)
    {
        var url = Sanitize(candidate, allowList, fallback);
        // A relative path is same-site (LocalRedirect-safe). An allow-listed absolute URL — or an
        // absolute configured fallback — is a deliberate cross-origin redirect (plain Redirect).
        var isLocal = Uri.TryCreate(url, UriKind.Relative, out _);
        return new Resolution(url, isLocal);
    }

    /// <summary>
    /// WEB-0078: a rooted relative URL placed under <paramref name="pathBase"/>; an absolute URL, an empty one, a
    /// non-rooted one, or one that already begins with the exact path base (ordinal, at a segment boundary) is
    /// unchanged. The comparison is ordinal because a proxy may route the path base case-sensitively, so
    /// <c>/TANGENT/x</c> under <c>/tangent</c> becomes <c>/tangent/TANGENT/x</c>.
    /// </summary>
    public static string UnderPathBase(string url, PathString pathBase)
    {
        if (!pathBase.HasValue || string.IsNullOrEmpty(url) || !url.StartsWith('/')) return url;
        var prefix = pathBase.ToUriComponent();
        if (url.StartsWith(prefix, StringComparison.Ordinal) && (url.Length == prefix.Length || url[prefix.Length] is '/' or '?' or '#'))
            return url;
        return prefix + url;
    }

    private static string Sanitize(string? candidate, IReadOnlyList<string>? allowList, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        if (Uri.TryCreate(candidate, UriKind.Relative, out _)) return candidate; // same-site path
        if (allowList is not null &&
            allowList.Any(p => candidate.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return candidate; // allow-listed absolute (cross-origin) URL
        return fallback;
    }
}
