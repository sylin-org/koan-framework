using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Services;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Recs)] // Controllers only per Koan guideline
public class RecsController(IRecsService recs) : ControllerBase
{
    [HttpPost("query")]
    public IActionResult Query([FromBody] RecsQuery req)
    {
        // If userId is not provided but caller is authenticated, derive from claims
        string? userIdOverride = null;
        if (string.IsNullOrWhiteSpace(req.UserId) && HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            userIdOverride = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                           ?? HttpContext.User.FindFirst("sub")?.Value;
        }

        var ct = HttpContext?.RequestAborted ?? CancellationToken.None;
        var (items, degraded) = recs.QueryAsync(req, userIdOverride, ct).GetAwaiter().GetResult();
        return Ok(new { items, degraded });
    }

    [HttpPost("rate")]
    public IActionResult Rate([FromBody] RateRequest req)
    {
        recs.RateAsync(req.UserId, req.MediaId, req.Rating, HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { ok = true });
    }
}