using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Flow.Builtin;

/// <summary>
/// Built-in <see cref="IKoanAuthFlowHandler"/> that converts cookie-auth's default 302 redirects
/// into 401 (challenge) and 403 (access denied) responses when the request looks like XHR or JSON.
/// Replaces the inline <c>WantsJson()</c> heuristic that previously lived in the cookie-event
/// hookers; that logic now ships as a discoverable, configurable, replaceable handler.
/// </summary>
/// <remarks>
/// <para>
/// Runs early (Priority = <see cref="int.MinValue"/> + 1000) so a handler that wants to override
/// it can register at a lower priority and short-circuit by marking
/// <see cref="AuthChallengeContext.ResponseHandled"/> or
/// <see cref="AuthAccessDeniedContext.ResponseHandled"/>. Honors
/// <see cref="ChallengeOptions.Enabled"/> = false by no-oping silently.
/// </para>
/// </remarks>
public sealed class JsonChallengeHandler : IKoanAuthFlowHandler
{
    private readonly IOptionsMonitor<ChallengeOptions> _options;

    public JsonChallengeHandler(IOptionsMonitor<ChallengeOptions> options)
    {
        _options = options;
    }

    /// <summary>Built-ins run before app handlers by default; an app handler can still preempt by registering at a lower priority.</summary>
    public int Priority => int.MinValue + 1000;

    public Task OnChallenge(AuthChallengeContext ctx, CancellationToken ct)
    {
        if (ctx.ResponseHandled) return Task.CompletedTask;
        var opts = _options.CurrentValue;
        if (!opts.Enabled) return Task.CompletedTask;
        if (!IsApiShaped(ctx.HttpContext.Request, opts)) return Task.CompletedTask;

        ctx.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.ResponseHandled = true;
        return Task.CompletedTask;
    }

    public Task OnAccessDenied(AuthAccessDeniedContext ctx, CancellationToken ct)
    {
        if (ctx.ResponseHandled) return Task.CompletedTask;
        var opts = _options.CurrentValue;
        if (!opts.Enabled) return Task.CompletedTask;
        if (!IsApiShaped(ctx.HttpContext.Request, opts)) return Task.CompletedTask;

        ctx.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.ResponseHandled = true;
        return Task.CompletedTask;
    }

    internal static bool IsApiShaped(HttpRequest req, ChallengeOptions opts)
    {
        if (opts.TreatAcceptJsonAsApi)
        {
            var accept = req.Headers.Accept.ToString();
            if (!string.IsNullOrEmpty(accept) &&
                accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        if (opts.TreatXhrHeaderAsApi)
        {
            var xhr = req.Headers["X-Requested-With"].ToString();
            if (!string.IsNullOrEmpty(xhr) &&
                string.Equals(xhr, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (opts.ApiPaths is { Count: > 0 })
        {
            foreach (var prefix in opts.ApiPaths)
            {
                if (string.IsNullOrEmpty(prefix)) continue;
                if (req.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
