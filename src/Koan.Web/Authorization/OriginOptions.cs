using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 — declares which source networks count as <see cref="OriginTier.Internal"/> (the homelab/small-team
/// "trusted LAN" case). Bound from <c>Koan:Web:Origin</c>. FAIL-CLOSED by design: an empty list means no address is
/// ever internal, so <c>origin:internal</c> can never become a spoofable boundary — a homelabber opts in by
/// declaring their LAN CIDR(s) once. The effective client IP must already be correct
/// (<c>UseForwardedHeaders</c> is the app's responsibility when behind a trusted proxy).
/// </summary>
public sealed class OriginOptions
{
    /// <summary>Configuration section bound to these options.</summary>
    public const string SectionPath = "Koan:Web:Origin";

    /// <summary>The shared empty (fail-closed) options used when none are registered.</summary>
    public static readonly OriginOptions Empty = new();

    /// <summary>CIDR networks treated as internal, e.g. <c>"192.168.0.0/16"</c>, <c>"10.0.0.0/8"</c>. Empty = none.</summary>
    public IList<string> InternalNetworks { get; } = new List<string>();

    /// <summary>True when <paramref name="ip"/> falls inside any declared <see cref="InternalNetworks"/> CIDR.</summary>
    public bool IsInternal(IPAddress? ip)
    {
        if (ip is null || InternalNetworks.Count == 0) return false;
        foreach (var cidr in InternalNetworks)
        {
            if (IPNetwork.TryParse(cidr, out var network) && network.Contains(ip)) return true;
        }
        return false;
    }
}

/// <summary>
/// SEC-0004 — resolves the <see cref="OriginTier"/> for a networked (HTTP) caller from its source address. NEVER
/// returns <see cref="OriginTier.Local"/> (local is STDIO-exclusive, OS-guaranteed); a networked caller is
/// <see cref="OriginTier.Internal"/> only when its IP is in a declared trusted network, else
/// <see cref="OriginTier.Remote"/> (the safe default — including loopback HTTP unless explicitly declared internal).
/// </summary>
public static class OriginResolver
{
    public static OriginTier FromHttpContext(HttpContext httpContext, OriginOptions options)
        => FromIp(httpContext?.Connection?.RemoteIpAddress, options);

    public static OriginTier FromIp(IPAddress? ip, OriginOptions options)
        => (options ?? OriginOptions.Empty).IsInternal(ip) ? OriginTier.Internal : OriginTier.Remote;
}
