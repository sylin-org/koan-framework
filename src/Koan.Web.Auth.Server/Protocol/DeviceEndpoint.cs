using Koan.Data.Core;
using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D8 — the Device Authorization Request (RFC 8628 §3.1). A headless client posts <c>client_id</c>
/// (+ scope + resource) and receives a <c>device_code</c> (it polls <c>/oauth/token</c> with) and a short
/// <c>user_code</c> the user enters on a second device at <c>verification_uri</c> (the app's consent page).
/// </summary>
internal static class DeviceEndpoint
{
    internal static async Task Device(HttpContext ctx)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);

        var clientId = form["client_id"].ToString();
        var resource = form["resource"].ToString();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            await Error(ctx, "invalid_request", "client_id is required.");
            return;
        }
        var client = await OAuthClient.Get(clientId, ctx.RequestAborted);
        if (client is null || !client.IsActive(now))
        {
            await Error(ctx, "invalid_client", "Unknown client.");
            return;
        }
        if (string.IsNullOrWhiteSpace(resource))
        {
            await Error(ctx, "invalid_request", "A resource indicator (RFC 8707) is required.");
            return;
        }

        var device = new DeviceCode
        {
            Id = OpaqueToken.New(),          // device_code — opaque, never logged
            UserCode = UserCode.New(),       // normalized lookup form
            ClientId = clientId,
            Scope = form["scope"].ToString(),
            Resource = resource,
            Status = DeviceCode.StatusPending,
            IntervalSeconds = options.DevicePollIntervalSeconds,
            ExpiresUtc = now + options.DeviceCodeLifetime,
        };
        await device.Save(ctx.RequestAborted);

        var verificationUri = RequestHost.Url(ctx, AppPaths.Consent(ctx, options));
        ctx.Response.Headers.CacheControl = "no-store"; // RFC 8628 §3.2
        await ctx.Response.WriteAsJsonAsync(new
        {
            device_code = device.Id,
            user_code = UserCode.Format(device.UserCode),
            verification_uri = verificationUri,
            verification_uri_complete = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
                verificationUri, "user_code", UserCode.Format(device.UserCode)),
            expires_in = (int)options.DeviceCodeLifetime.TotalSeconds,
            interval = device.IntervalSeconds,
        }, cancellationToken: ctx.RequestAborted);
    }

    private static Task Error(HttpContext ctx, string error, string description)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return ctx.Response.WriteAsJsonAsync(new { error, error_description = description }, cancellationToken: ctx.RequestAborted);
    }
}
