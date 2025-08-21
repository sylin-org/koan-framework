using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Web.Infrastructure;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route(SoraWebConstants.Routes.HealthBase)]
public sealed class HealthController(IHostEnvironment env, IHealthAggregator aggregator) : ControllerBase
{
    // Human-friendly info endpoint; simple up check (no dependencies)
    [HttpGet]
    public IActionResult Info() => Ok(new { status = "ok" });

    // Liveness: process is running; no dependency checks
    [HttpGet(SoraWebConstants.Routes.HealthLive)]
    public IActionResult Live() => Ok(new { status = "healthy" });

    // Readiness: dependencies are ready; returns 503 when any critical check is unhealthy
    [HttpGet(SoraWebConstants.Routes.HealthReady)]
    [Produces("application/health+json", "application/json")]
    public async Task<IActionResult> Ready()
    {
        await Task.Yield(); // keep signature async-friendly
        var snap = aggregator.GetSnapshot();

        object payload = env.IsDevelopment()
            ? new
            {
                status = snap.Overall.ToString().ToLowerInvariant(),
                components = snap.Components
                    .Select(c => new
                    {
                        name = c.Component,
                        state = c.Status.ToString().ToLowerInvariant(),
                        message = c.Message,
                        ttl = c.Ttl?.ToString(),
                        facts = c.Facts
                    })
            }
            : new { status = snap.Overall.ToString().ToLowerInvariant() };

        if (snap.Overall == HealthStatus.Unhealthy)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

        Response.Headers["Cache-Control"] = SoraWebConstants.Policies.NoStore;
        return Ok(payload);
    }
}
