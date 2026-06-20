using System;
using System.Linq;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// WEB-0071 — resolves a RELATIVE server-facing endpoint (e.g. the dev Test IdP's <c>/.testoauth</c> base) to an
/// in-network absolute URL the maintained handler's back-channel can reach.
/// </summary>
/// <remarks>
/// The blind spot this fixes: Kestrel's wildcard bind forms — <c>http://+:8080</c>, <c>http://*:8080</c> — are NOT
/// parseable by <see cref="Uri"/> (<see cref="Uri.TryCreate(string, UriKind, out Uri)"/> returns false), which is the
/// most common <c>ASPNETCORE_URLS</c> value in a container. The previous code parsed with <see cref="Uri"/> and so
/// left the endpoint relative, and the OIDC back-channel then threw "request URI must be absolute". We parse the bind
/// string structurally instead, mapping any-address hosts (<c>+</c>, <c>*</c>, <c>0.0.0.0</c>, <c>[::]</c>, empty) to
/// loopback — the address at which the app reaches its OWN self-hosted endpoints. Chiseled .NET images set
/// <c>ASPNETCORE_HTTP_PORTS</c>/<c>ASPNETCORE_HTTPS_PORTS</c> instead of <c>ASPNETCORE_URLS</c>, so those are consulted
/// as a fallback. This is a self-hosted-IdP concern only; real providers ship absolute endpoints (returned verbatim).
/// </remarks>
internal static class ServerAddressResolver
{
    private static readonly char[] ListSeparators = [';', ',', ' '];

    /// <summary>Make a possibly-relative endpoint absolute against the in-network base; absolute inputs pass through.</summary>
    public static string? ToAbsolute(string? endpoint, string? aspnetcoreUrls, string? httpsPorts, string? httpPorts)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out _)) return endpoint;

        var baseUrl = ResolveBase(aspnetcoreUrls, httpsPorts, httpPorts);
        if (baseUrl is null) return endpoint; // relative; back-channel BaseAddress (if any) resolves it

        var path = endpoint.StartsWith('/') ? endpoint : "/" + endpoint;
        return baseUrl + path;
    }

    /// <summary>The in-network base (scheme://host:port) from the bind config, or null when none is resolvable.</summary>
    public static string? ResolveBase(string? aspnetcoreUrls, string? httpsPorts, string? httpPorts)
    {
        var raw = aspnetcoreUrls?
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(p => p.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                              || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        if (raw is not null && TryParseBind(raw, out var fromUrls)) return fromUrls;

        // ASPNETCORE_*_PORTS bind on any address → loopback is the reachable in-network host.
        if (FirstPort(httpsPorts) is { } sp) return $"https://localhost:{sp}";
        if (FirstPort(httpPorts) is { } hp) return $"http://localhost:{hp}";
        return null;
    }

    private static string? FirstPort(string? ports) => ports?
        .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(p => p.All(char.IsDigit));

    /// <summary>Parse one bind string into scheme://host:port, mapping wildcard/any-address hosts to loopback.</summary>
    private static bool TryParseBind(string url, out string? baseUrl)
    {
        baseUrl = null;
        var sep = url.IndexOf("://", StringComparison.Ordinal);
        if (sep <= 0) return false;
        var scheme = url[..sep].ToLowerInvariant();
        var rest = url[(sep + 3)..].TrimEnd('/');
        if (rest.Length == 0) return false;

        string host;
        int port;
        if (rest.StartsWith('[')) // IPv6 literal, e.g. [::]:8080
        {
            var close = rest.IndexOf(']');
            if (close < 0) return false;
            host = rest[..(close + 1)];
            port = ParsePort(rest[(close + 1)..], scheme);
            if (host is "[::]" or "[0:0:0:0:0:0:0:0]") host = "localhost";
        }
        else
        {
            var colon = rest.LastIndexOf(':');
            if (colon < 0) { host = rest; port = DefaultPort(scheme); }
            else { host = rest[..colon]; port = ParsePort(rest[colon..], scheme); }
        }

        if (host is "+" or "*" or "0.0.0.0" or "") host = "localhost";
        baseUrl = $"{scheme}://{host}:{port}";
        return true;
    }

    private static int ParsePort(string colonPort, string scheme)
    {
        var p = colonPort.TrimStart(':');
        return int.TryParse(p, out var n) ? n : DefaultPort(scheme);
    }

    private static int DefaultPort(string scheme) => scheme == "https" ? 443 : 80;
}
