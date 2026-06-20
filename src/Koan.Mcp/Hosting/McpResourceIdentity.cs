using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Koan.Mcp.Hosting;

/// <summary>
/// SEC-0006 D2 — the MCP HTTP/SSE edge as an OAuth 2.1 <b>resource server</b>. Derives the canonical resource
/// id from the live request host (forwarded-aware via <see cref="HttpRequest.Scheme"/>/<see cref="HttpRequest.Host"/>),
/// enforces the RFC 8707 audience (<c>aud == this resource</c>), and builds the RFC 9728 discovery hints
/// (<c>WWW-Authenticate</c> + the protected-resource metadata URL) clients use to find the Authorization Server.
/// <para>
/// The audience check is the confused-deputy fix: a token minted for another resource (a sibling API, a
/// service-mesh token with <c>aud=koan</c>) is authentic but carries the wrong audience, so it is rejected
/// here even though the bearer scheme validated its signature.
/// </para>
/// </summary>
public static class McpResourceIdentity
{
    /// <summary>
    /// The canonical resource id for this MCP edge. Prefers the configured fixed identifier
    /// (<see cref="Koan.Mcp.Options.McpServerOptions.ResourceUri"/>) — the correct, host-spoof-proof posture
    /// behind a proxy — and falls back to the live request host (the Development default).
    /// </summary>
    public static string Resolve(HttpContext ctx, string baseRoute, string? configuredResource = null)
        => !string.IsNullOrWhiteSpace(configuredResource)
            ? configuredResource.TrimEnd('/')
            : $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}{baseRoute}";

    /// <summary>The Authorization Server base (host root) — where RFC 8414 metadata + the AS endpoints live.</summary>
    public static string AuthorizationServer(HttpContext ctx)
        => $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";

    /// <summary>The RFC 9728 protected-resource metadata URL for this edge.</summary>
    public static string MetadataUrl(HttpContext ctx, string baseRoute)
        => $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}/.well-known/oauth-protected-resource{baseRoute}";

    /// <summary>
    /// True when the principal's <c>aud</c> claim set contains <paramref name="resource"/>. A credential with
    /// no <c>aud</c> at all is treated as unbound and does NOT match a specific resource. Comparison is
    /// case-sensitive (<c>Ordinal</c>) — RFC 7519 §4.1.3 makes <c>aud</c> a case-sensitive string.
    /// </summary>
    public static bool AudienceMatches(ClaimsPrincipal user, string resource)
        => user.FindAll("aud").Any(c => string.Equals(c.Value, resource, StringComparison.Ordinal));

    /// <summary>
    /// Emit an RFC 6750 / RFC 9728 bearer challenge: 401 with a <c>WWW-Authenticate</c> header pointing at the
    /// protected-resource metadata, so a conformant MCP client discovers the AS and (re)authorizes for this
    /// resource. <paramref name="error"/>/<paramref name="description"/> are included when a token was present
    /// but unacceptable (e.g. wrong audience).
    /// </summary>
    public static string BuildChallenge(HttpContext ctx, string baseRoute, string? error = null, string? description = null)
    {
        var metadata = MetadataUrl(ctx, baseRoute);
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(error)) parts.Add($"error=\"{error}\"");
        if (!string.IsNullOrEmpty(description)) parts.Add($"error_description=\"{description}\"");
        parts.Add($"resource_metadata=\"{metadata}\"");
        return "Bearer " + string.Join(", ", parts);
    }
}
