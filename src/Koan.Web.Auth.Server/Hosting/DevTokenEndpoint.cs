using Koan.Web.Auth.Server.Options;
using Koan.Security.Trust.Issuer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Hosting;

/// <summary>
/// SEC-0006 — the <c>GET /oauth/dev-token</c> convenience endpoint. Mints a real ES256 access token
/// for the <b>currently signed-in cookie user</b>, bound (RFC 8707) to the requested resource (default: this
/// host's MCP edge). It exists to validate the whole token → bearer → SEC-0004/0005 chain end-to-end without
/// standing up a full OAuth client.
/// <para>
/// <b>Hard dev-gate:</b> it returns 404 outside the Development environment — even if <see cref="AuthServerOptions.DevTokenEnabled"/>
/// is true — so a production build can never mint a token from a bare cookie session (that path is the full
/// authorization-code flow). This deliberately rejects the dev Test provider's "Enabled ships it everywhere" footgun.
/// </para>
/// </summary>
internal static class DevTokenEndpoint
{
    internal static async Task HandleAsync(HttpContext ctx)
    {
        var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;

        // HARD dev-gate, fail-closed: 404 (don't even acknowledge the endpoint) outside Development or when disabled.
        if (!env.IsDevelopment() || !options.DevTokenEnabled)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "login_required",
                message = "Sign in to the app first (cookie session), then retry GET /oauth/dev-token.",
            }, cancellationToken: ctx.RequestAborted);
            return;
        }

        // Resource (RFC 8707): the audience to bind. Default to this host's MCP resource so the minted token is
        // immediately usable against the local /mcp edge. An explicit ?resource= must be an absolute http(s) URI.
        var resource = ctx.Request.Query["resource"].ToString();
        if (string.IsNullOrWhiteSpace(resource))
        {
            resource = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}/mcp";
        }
        else if (!Uri.TryCreate(resource, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "invalid_resource",
                message = "The 'resource' parameter must be an absolute http(s) URI.",
            }, cancellationToken: ctx.RequestAborted);
            return;
        }

        // Dev-only testing knobs: ?scope=<space-delimited> and ?roles=<space/comma-delimited> mint exactly those
        // into the token (as-is, no held-filter) so a scope-gated [McpTool] / [Access(has:scope:x)] path is
        // exercisable with a one-line curl. Absent → the session's own scopes/roles.
        var scopeOverride = ParseList(ctx.Request.Query["scope"].ToString());
        var rolesOverride = ParseList(ctx.Request.Query["roles"].ToString());

        var issuer = ctx.RequestServices.GetRequiredService<IIssuer>();
        var claims = SessionPrincipal.ToTrustClaims(ctx.User, clientId: "koan-dev-token", scopeOverride, rolesOverride);
        // Clamp to a sane ceiling so a misconfigured option can't mint a long-lived dev credential.
        var minutes = Math.Clamp(options.DevTokenLifetimeMinutes, 1, 1440);
        var lifetime = TimeSpan.FromMinutes(minutes);
        var token = issuer.Issue(claims, lifetime, audience: resource);

        await ctx.Response.WriteAsJsonAsync(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = (int)lifetime.TotalSeconds,
            resource,
            scope = string.Join(' ', claims.Scopes),
        }, cancellationToken: ctx.RequestAborted);
    }

    // Accepts space- OR comma-delimited input; null when the query param was absent (so the session value is kept).
    private static IReadOnlyCollection<string>? ParseList(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Distinct(StringComparer.Ordinal).ToArray();
}
