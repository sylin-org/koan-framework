using Microsoft.AspNetCore.Mvc;

using S5.Recs.Services;
using S5.Recs.Infrastructure;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Recs)] // Controllers only per Sora guideline
public class RecsController(IRecsService recs) : ControllerBase
{
    [HttpPost("query")]
    public IActionResult Query([FromBody] RecsQuery req)
    {
    var (items, degraded) = recs.QueryAsync(
        req.Text,
        req.AnchorAnimeId,
        req.Filters?.Genres,
        req.Filters?.EpisodesMax,
        req.Filters?.SpoilerSafe ?? true,
        req.TopK,
        req.UserId,
        req.Filters?.PreferTags,
        req.Filters?.PreferWeight,
        HttpContext.RequestAborted).GetAwaiter().GetResult();
    return Ok(new { items, degraded });
    }

    [HttpPost("rate")]
    public IActionResult Rate([FromBody] RateRequest req)
    {
        recs.RateAsync(req.UserId, req.AnimeId, req.Rating, HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { ok = true });
    }
}