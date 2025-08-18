using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using System.Linq;
using System.Threading.Tasks;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class HealthController(IServiceScopeFactory scopeFactory) : ControllerBase
{
    // API health endpoint - simple status check
    [HttpGet("api/health")]
    public IActionResult Get() => Ok(new { status = "ok" });

    // Liveness probe - stays healthy unless process is failing hard
    [HttpGet("/health/live")]
    public IActionResult Live()
    {
        return Ok(new { status = "healthy" });
    }

    // Readiness probe - aggregate contributors; unhealthy only if a critical dependency is unhealthy
    [HttpGet("/health/ready")]
    public async Task<IActionResult> Ready()
    {
        using var scope = scopeFactory.CreateScope();
        var hs = scope.ServiceProvider.GetRequiredService<IHealthService>();
        var (overall, reports) = await hs.CheckAllAsync(HttpContext.RequestAborted);
        
        var payload = new
        {
            status = overall.ToString().ToLowerInvariant(),
            details = reports.Select(r => new
            {
                name = r.Name,
                state = r.State.ToString().ToLowerInvariant(),
                description = r.Description,
                data = r.Data
            })
        };
        
        if (overall == HealthState.Unhealthy)
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            
        return Ok(payload);
    }
}
