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
        string? userId = req.UserId;
        if (string.IsNullOrWhiteSpace(userId) && HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            userId = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? HttpContext.User.FindFirst("sub")?.Value;
        }

        var ct = HttpContext?.RequestAborted ?? CancellationToken.None;
        var (items, degraded) = recs.QueryAsync(
            req.Text,
            req.AnchorMediaId,
            req.Filters?.Genres,
            req.Filters?.EpisodesMax,
            req.Filters?.SpoilerSafe ?? true,
            req.TopK,
            userId,
            req.Filters?.PreferTags,
            req.Filters?.PreferWeight,
            req.Sort,
            req.Filters?.MediaType,
            req.Filters?.RatingMin,
            req.Filters?.RatingMax,
            req.Filters?.YearMin,
            req.Filters?.YearMax,
            req.Alpha,
            ct).GetAwaiter().GetResult();
        return Ok(new { items, degraded });
    }

    [HttpPost("rate")]
    public IActionResult Rate([FromBody] RateRequest req)
    {
        recs.RateAsync(req.UserId, req.MediaId, req.Rating, HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { ok = true });
    }
}