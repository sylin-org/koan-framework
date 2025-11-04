using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Services;
using S5.Recs.Services.Pagination;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Recs)] // Controllers only per Koan guideline
public class RecsController(IRecsService recs, IBandCacheService bandCache) : ControllerBase
{
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] RecsQuery req)
    {
        // If userId is not provided but caller is authenticated, derive from claims
        string? userIdOverride = null;
        if (string.IsNullOrWhiteSpace(req.UserId) && HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            userIdOverride = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                           ?? HttpContext.User.FindFirst("sub")?.Value;
        }

        var ct = HttpContext?.RequestAborted ?? CancellationToken.None;

        // ADR-0052: Use sliding window cache for offset/limit pagination
        // Fall back to legacy topK approach if ExcludeIds is provided (client-side deduplication)
        bool useSlidingWindow = req.Offset.HasValue && req.Limit.HasValue && (req.ExcludeIds == null || req.ExcludeIds.Length == 0);

        if (useSlidingWindow)
        {
            // New pagination: Use band cache service
            var (items, degraded) = await bandCache.GetPageAsync(
                req,
                req.Offset!.Value,
                req.Limit!.Value,
                userIdOverride,
                ct);
            return Ok(new { items, degraded });
        }
        else
        {
            // Legacy pagination: Use original RecsService logic
            var (items, degraded) = await recs.QueryAsync(req, userIdOverride, ct);
            return Ok(new { items, degraded });
        }
    }

    [HttpPost("rate")]
    public IActionResult Rate([FromBody] RateRequest req)
    {
        recs.RateAsync(req.UserId, req.MediaId, req.Rating, HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { ok = true });
    }
}