using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using System.Linq;
using System.Threading.Tasks;
using Sora.Web.Infrastructure;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route(SoraWebConstants.Routes.HealthBase)]
public sealed class HealthController(IHealthService health, IHostEnvironment env) : ControllerBase
{
    // Human-friendly info endpoint; simple up check (no dependencies)
    [HttpGet]
    [HttpGet(SoraWebConstants.Routes.ApiHealth)] // legacy alias
    public IActionResult Info() => Ok(new { status = "ok" });

    // Liveness: process is running; no dependency checks
    [HttpGet(SoraWebConstants.Routes.HealthLive)]
    public IActionResult Live() => Ok(new { status = "healthy" });

    // Readiness: dependencies are ready; returns 503 when any critical check is unhealthy
    [HttpGet(SoraWebConstants.Routes.HealthReady)]
    [Produces("application/health+json")]
    public async Task<IActionResult> Ready()
    {
        var (overall, reports) = await health.CheckAllAsync(HttpContext.RequestAborted);

        object payload = env.IsDevelopment()
            ? new
            {
                status = overall.ToString().ToLowerInvariant(),
                details = reports.Select(r => new
                {
                    name = r.Name,
                    state = r.State.ToString().ToLowerInvariant(),
                    description = r.Description,
                    data = r.Data
                })
            }
            : new { status = overall.ToString().ToLowerInvariant() };

        if (overall == HealthState.Unhealthy)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

    Response.Headers["Cache-Control"] = SoraWebConstants.Policies.NoStore;
        return Ok(payload);
    }
}
