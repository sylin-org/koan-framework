using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Web.Auth.Options;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// SEC-0001 §10 — single source of truth for resolving a post-authentication return URL and the
/// kind of redirect it warrants. The allow-list is the security boundary (WEB-0043): a local path of
/// this application, or an absolute URL under an allow-listed origin and path (the cross-origin case the
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
    /// <paramref name="candidate"/> when it is a local path under <paramref name="pathBase"/>, or, as parsed, when it is
    /// an absolute URL under an allow-listed entry (<see cref="AllowListed"/>); else <paramref name="fallback"/>.
    /// </summary>
    public static Resolution Resolve(string? candidate, IReadOnlyList<string>? allowList, string fallback, PathString pathBase = default)
    {
        if (IsLocalPath(candidate, pathBase)) return new Resolution(candidate!, true);
        if (AllowListed(candidate, allowList) is { } absolute)
            return new Resolution(absolute, false); // allow-listed absolute (cross-origin) URL
        return new Resolution(fallback, fallback.StartsWith('/') && !fallback.StartsWith("//", StringComparison.Ordinal));
    }

    /// <summary>
    /// SEC-0029 §2 — <paramref name="candidate"/> as the ASCII URL that was compared (<see cref="Compared"/>), when it is
    /// an absolute http(s) URL without userinfo whose scheme, host (in its IDNA form) and port equal an allow-list
    /// entry's and whose path, holding no encoded slash or backslash and no <c>.</c> or <c>..</c> segment after
    /// percent-decoding, lies under the entry's path at a segment boundary; else null. Origins are compared, not strings:
    /// under <c>https://app.example</c>, <c>https://app.example.evil.com/</c>, <c>https://app.example@evil.com/</c> and
    /// <c>https://app.example:8443/</c> are other origins, and under <c>https://partner.example.com/cb</c>,
    /// <c>/cb/..%2Fadmin</c> is refused because a server that decodes before it resolves dot segments serves
    /// <c>/admin</c>. The caller redirects to the returned URL, so the browser receives exactly what was compared.
    /// </summary>
    private static string? AllowListed(string? candidate, IReadOnlyList<string>? allowList)
    {
        if (string.IsNullOrWhiteSpace(candidate) || allowList is null || allowList.Count == 0) return null;
        if (!TryAbsoluteHttp(candidate, out var absolute) || absolute.UserInfo.Length > 0) return null;
        if (!SegmentsArePlain(absolute.AbsolutePath) || Compared(absolute) is not { } compared) return null;
        foreach (var entry in allowList)
        {
            if (!IsAllowListEntry(entry) || !TryAbsoluteHttp(entry, out var origin)) continue;
            if (!string.Equals(absolute.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(absolute.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase)
                || absolute.Port != origin.Port)
                continue;
            var under = origin.AbsolutePath.TrimEnd('/');
            var path = absolute.AbsolutePath;
            if (under.Length == 0
                || path.StartsWith(under, StringComparison.Ordinal) && (path.Length == under.Length || path[under.Length] == '/'))
                return compared;
        }
        return null;
    }

    /// <summary>
    /// SEC-0029 §2 — true when <paramref name="entry"/> can be an allow-list entry: an absolute http(s) URL naming an
    /// origin and, optionally, a path under it, with no userinfo, query or fragment. Any other entry refuses the start.
    /// </summary>
    internal static bool IsAllowListEntry(string? entry) =>
        !string.IsNullOrWhiteSpace(entry)
        && entry.IndexOfAny(['?', '#']) < 0
        && TryAbsoluteHttp(entry, out var origin)
        && origin.UserInfo.Length == 0
        && origin.Query.Length == 0
        && origin.Fragment.Length == 0;

    /// <summary>
    /// SEC-0029 §3 — true when <paramref name="defaultPath"/> can be <c>ReturnUrl:DefaultPath</c>: an application-relative
    /// path that, rooted as <see cref="DefaultLanding"/> roots it, is a local path (<see cref="IsLocalPath"/>), or an
    /// absolute URL under an entry of <paramref name="allowList"/>. Any other value refuses the start, so the default
    /// landing never leaves the application except through the allow-list.
    /// </summary>
    internal static bool AcceptsDefaultPath(string? defaultPath, IReadOnlyList<string>? allowList)
    {
        if (string.IsNullOrWhiteSpace(defaultPath)) return true;
        if (TryAbsoluteHttp(defaultPath, out var absolute))
            return AllowListed(defaultPath, allowList) is not null && Compared(absolute) is not null;
        return IsLocalPath(defaultPath.StartsWith('/') ? defaultPath : "/" + defaultPath);
    }

    /// <summary>
    /// The URL built from exactly what the allow-list compares: scheme, the host's IDNA (ASCII) form, the port when it is
    /// not the scheme's default, and the escaped path, query and fragment. A Unicode spelling of a host
    /// (<c>ａｐｐ.example</c>) is therefore written as the host it was compared as (<c>app.example</c>), and the result is
    /// a valid <c>Location</c> header; null when it would still not be printable ASCII.
    /// </summary>
    private static string? Compared(Uri absolute)
    {
        var host = absolute.IdnHost;
        if (absolute.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('[')) host = $"[{host}]";
        var port = absolute.IsDefaultPort ? string.Empty : $":{absolute.Port}";
        var url = $"{absolute.Scheme}://{host}{port}{absolute.PathAndQuery}{absolute.Fragment}";
        return url.All(static c => c is > ' ' and <= '~') ? url : null;
    }

    /// <summary>
    /// True when no segment of <paramref name="escapedPath"/>, percent-decoded with the local-path rule's passes, holds a
    /// slash, a backslash or a control character, or is <c>.</c> or <c>..</c>.
    /// </summary>
    private static bool SegmentsArePlain(string escapedPath)
    {
        foreach (var segment in escapedPath.Split('/'))
        {
            var decoded = segment;
            for (var pass = 0; pass < MaximumDecodingPasses && decoded.Contains('%'); pass++)
                decoded = Uri.UnescapeDataString(decoded);
            if (decoded is "." or ".." || decoded.Any(static c => c is '/' or '\\' or < ' ' or (char)0x7F)) return false;
        }
        return true;
    }

    private static bool TryAbsoluteHttp(string value, out Uri absolute)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            absolute = parsed;
            return true;
        }
        absolute = null!;
        return false;
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
        // An absolute default is written as the allow-list compared it at start (SEC-0029 §2), so its host is ASCII.
        if (TryAbsoluteHttp(defaultPath, out var absolute)) return Compared(absolute) ?? ApplicationPaths.Under(context, "/");
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
