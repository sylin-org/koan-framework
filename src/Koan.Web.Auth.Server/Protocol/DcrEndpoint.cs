using System.Text.Json;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D5 — Dynamic Client Registration (RFC 7591). Open by default (Claude Desktop has no pre-shared
/// client_id) but zero-trust: every registered client is forced <b>public</b> (no secret, PKCE-required),
/// constrained to <b>loopback</b> redirect URIs, rate-limited per source + globally, and TTL-expired. The
/// <c>client_name</c>/<c>logo_uri</c> are stored untrusted (displayed escaped + "unverified" on consent).
/// </summary>
internal sealed class DcrEndpoint : IKoanEndpointContributor
{
    public void Map(IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/oauth/register", Register).ExcludeFromDescription();

    private static async Task Register(HttpContext ctx)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var limiter = ctx.RequestServices.GetRequiredService<FixedWindowRateLimiter>();

        if (!options.AllowDynamicRegistration)
        {
            await Error(ctx, StatusCodes.Status403Forbidden, "access_denied", "Dynamic client registration is disabled.");
            return;
        }

        // D5 — rate-limit the open endpoint per source IP and globally.
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = TimeSpan.FromMinutes(1);
        if (!limiter.TryAcquire("dcr:ip:" + ip, options.RegistrationRateLimitPerMinute, window, now)
            || !limiter.TryAcquire("dcr:global", options.RegistrationRateLimitGlobalPerMinute, window, now))
        {
            await Error(ctx, StatusCodes.Status429TooManyRequests, "temporarily_unavailable", "Too many registration requests.");
            return;
        }

        JsonElement body;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<JsonElement>(ctx.RequestAborted);
        }
        catch (JsonException)
        {
            await Error(ctx, StatusCodes.Status400BadRequest, "invalid_client_metadata", "Request body must be JSON.");
            return;
        }

        var redirectUris = ReadStringArray(body, "redirect_uris");
        if (redirectUris.Count == 0)
        {
            await Error(ctx, StatusCodes.Status400BadRequest, "invalid_redirect_uri", "At least one redirect_uri is required.");
            return;
        }
        // D5 — a dynamic client may NEVER register a non-loopback redirect.
        if (redirectUris.Any(u => !LoopbackRedirect.IsLoopback(u)))
        {
            await Error(ctx, StatusCodes.Status400BadRequest, "invalid_redirect_uri", "Dynamic clients may only register loopback redirect URIs (RFC 8252).");
            return;
        }

        var clientName = body.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? ""
            : "";

        var client = new OAuthClient
        {
            Id = "dcr_" + OpaqueToken.New(16),
            ClientName = clientName, // untrusted display-only
            RedirectUris = redirectUris,
            IsPublic = true,
            IsDynamic = true,
            CreatedUtc = now,
            ExpiresUtc = now + options.DynamicClientLifetime,
        };
        await client.Save(ctx.RequestAborted);

        ctx.Response.StatusCode = StatusCodes.Status201Created;
        await ctx.Response.WriteAsJsonAsync(new
        {
            client_id = client.Id,
            client_id_issued_at = now.ToUnixTimeSeconds(),
            client_secret_expires_at = 0, // public client — no secret
            client_name = client.ClientName,
            redirect_uris = client.RedirectUris,
            token_endpoint_auth_method = "none",
            grant_types = new[] { "authorization_code" },
            response_types = new[] { "code" },
        }, cancellationToken: ctx.RequestAborted);
    }

    private static List<string> ReadStringArray(JsonElement body, string property)
    {
        var list = new List<string>();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    list.Add(s);
        return list;
    }

    private static Task Error(HttpContext ctx, int status, string error, string description)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error, error_description = description }, cancellationToken: ctx.RequestAborted);
    }
}
