using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Koan.Web.Hosting;

namespace Koan.Tenancy.Web.Hosting;

/// <summary>
/// ARCH-0104 — the console EXPOSURE layer (routing, not authority). Runs at <see cref="KoanWebPipelineStage.BeforeRouting"/>
/// and short-circuits <b>404</b> for a console request that fails the configured exposure: the kill-switch
/// (<see cref="TenancyConsoleOptions.Enabled"/>), the host allow-list, or the required header. A miss returns 404 (the
/// surface is "not here"), distinct from the 403 the authority gate returns for an unadmitted operator. Non-console
/// paths pass straight through.
/// </summary>
internal sealed class TenancyConsoleExposureMiddleware
{
    private readonly RequestDelegate _next;

    public TenancyConsoleExposureMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOptions<TenancyConsoleOptions> options)
    {
        var path = context.Request.Path;
        var isConsole = path.StartsWithSegments(TenancyConsolePaths.UiPath, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWithSegments(TenancyConsolePaths.ApiPath, StringComparison.OrdinalIgnoreCase);

        if (isConsole && !Exposed(context, options.Value))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return; // surface is not exposed here — short-circuit before routing/endpoints
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool Exposed(HttpContext context, TenancyConsoleOptions o)
    {
        if (!o.Enabled) return false;

        // Ignore blank/whitespace host entries (a stray array slot is never a meaningful allow-list member) — so a
        // misconfigured Hosts:[""] behaves as "any" (matching the boot report) instead of silently 404ing every host.
        var hosts = o.Exposure.Hosts.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray();
        if (hosts.Length > 0 && !hosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(o.Exposure.RequireHeader) && !context.Request.Headers.ContainsKey(o.Exposure.RequireHeader))
            return false;

        return true;
    }
}

/// <summary>Mounts <see cref="TenancyConsoleExposureMiddleware"/> at <see cref="KoanWebPipelineStage.BeforeRouting"/>
/// (early short-circuit), via the ordering-safe pipeline seam (WEB-0069 — never <c>IStartupFilter</c> order).</summary>
internal sealed class TenancyConsoleExposureContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public void Configure(IApplicationBuilder app) => app.UseMiddleware<TenancyConsoleExposureMiddleware>();
}
