using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using System.Linq;
using System.Threading.Tasks;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class HealthEndpointsController(IServiceScopeFactory scopeFactory) : ControllerBase
{
    [HttpGet("/health/live")]
    public IActionResult Live()
    {
        return Ok(new { status = "healthy" });
    }

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
