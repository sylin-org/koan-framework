using Microsoft.AspNetCore.Mvc;

using S5.Recs.Services;
using S5.Recs.Infrastructure;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Admin)] // Sora guideline: controllers define routes
public class AdminController(ISeedService seeder, ILogger<AdminController> _logger, IEnumerable<Providers.IAnimeProvider> providers) : ControllerBase
{

    [HttpPost("seed/start")]
    public IActionResult StartSeed([FromBody] SeedRequest req)
    {
        var id = seeder.StartAsync(req.Source, req.Limit, req.Overwrite, HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { jobId = id });
    }

    [HttpGet("recs-settings")]
    public IActionResult GetRecsSettings([FromServices] S5.Recs.Services.IRecommendationSettingsProvider provider)
    {
        var (ptw, mpt, dw) = provider.GetEffective();
        return Ok(new { preferTagsWeight = ptw, maxPreferredTags = mpt, diversityWeight = dw });
    }

    public record RecsSettingsRequest(double PreferTagsWeight, int MaxPreferredTags, double DiversityWeight);

    [HttpPost("recs-settings")]
    public IActionResult SetRecsSettings([FromBody] RecsSettingsRequest req, [FromServices] S5.Recs.Services.IRecommendationSettingsProvider provider)
    {
    var ptw = Math.Clamp(req.PreferTagsWeight, 0, 1.0);
        var mpt = Math.Clamp(req.MaxPreferredTags, 1, 5);
        var dw = Math.Clamp(req.DiversityWeight, 0, 0.2);
        var doc = new Models.SettingsDoc { Id = "recs:settings", PreferTagsWeight = ptw, MaxPreferredTags = mpt, DiversityWeight = dw, UpdatedAt = DateTimeOffset.UtcNow };
        Models.SettingsDoc.UpsertMany(new[] { doc }, HttpContext.RequestAborted).GetAwaiter().GetResult();
        provider.InvalidateAsync(HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { preferTagsWeight = ptw, maxPreferredTags = mpt, diversityWeight = dw });
    }

    [HttpGet("seed/status/{jobId}")]
    public IActionResult GetStatus([FromRoute] string jobId)
    {
        var status = seeder.GetStatusAsync(jobId, HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(status);
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var (anime, contentPieces, vectors) = seeder.GetStatsAsync(HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { anime, contentPieces, vectors });
    }

    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        return Ok(providers.Select(p => new { code = p.Code, name = p.Name }));
    }

    [HttpPost("tags/rebuild")] // on-demand tag import/catalog rebuild
    public async Task<IActionResult> RebuildTags([FromServices] S5.Recs.Services.ISeedService seeder, CancellationToken ct)
    {
        var n = await seeder.RebuildTagCatalogAsync(ct);
        return Ok(new { updated = n });
    }

    [HttpPost("seed/vectors")] // vector-only upsert from existing docs
    public IActionResult StartVectorOnly([FromBody] VectorOnlyRequest req)
    {
        // Responsibility: AdminController builds the list; SeedService just upserts vectors for the provided items.
        var all = Models.AnimeDoc.All(HttpContext.RequestAborted).Result.ToList();

        _logger.LogInformation("------------- Starting vector-only upsert for {Count} items (limit {Limit})", all.Count, req.Limit);

        var id = seeder.StartVectorUpsertAsync(all, HttpContext.RequestAborted).Result;
        return Ok(new { jobId = id, count = all.Count });
    }

    // Minimal SSE for progress (poll-ish server push). Browsers: fetch('/admin/seed/sse/{jobId}').
    [HttpGet("seed/sse/{jobId}")]
    public async Task SeedSse([FromRoute] string jobId)
    {
        HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
        HttpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        HttpContext.Response.Headers.Append("Connection", "keep-alive");
        var ct = HttpContext.RequestAborted;
        while (!ct.IsCancellationRequested)
        {
            var status = await seeder.GetStatusAsync(jobId, ct);
            await HttpContext.Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(status)}\n\n", ct);
            await HttpContext.Response.Body.FlushAsync(ct);
            await Task.Delay(1000, ct);
        }
    }
}