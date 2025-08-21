using Microsoft.AspNetCore.Mvc;

using S5.Recs.Services;
using S5.Recs.Infrastructure;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Admin)] // Sora guideline: controllers define routes
public class AdminController(ISeedService seeder, IEnumerable<S5.Recs.Providers.IAnimeProvider> providers) : ControllerBase
{
    [HttpPost("seed/start")]
    public IActionResult StartSeed([FromBody] SeedRequest req)
    {
    var id = seeder.StartAsync(req.Source, req.Limit, req.Overwrite, HttpContext.RequestAborted).GetAwaiter().GetResult();
    return Ok(new { jobId = id });
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

public record SeedRequest(string Source = "local", int Limit = 50, bool Overwrite = false);
