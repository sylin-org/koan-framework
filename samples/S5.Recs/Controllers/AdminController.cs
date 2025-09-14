using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Services;

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

    // Censor tags admin
    [HttpGet("tags/censor")]
    public async Task<IActionResult> GetCensorTags(CancellationToken ct)
    {
        var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct);
        return Ok(new { tags = (doc?.Tags ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray() });
    }

    public record CensorTagsRequest(string? Text);

    [HttpPost("tags/censor/add")]
    public async Task<IActionResult> AddCensorTags([FromBody] CensorTagsRequest req, CancellationToken ct)
    {
        var src = req?.Text ?? string.Empty;
        var parts = src
            .Replace("\r", "\n")
            .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct) ?? new Models.CensorTagsDoc { Id = "recs:censor-tags" };
        var set = new HashSet<string>(doc.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts) set.Add(p);
        doc.Tags = set.OrderBy(s => s).ToList();
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await Models.CensorTagsDoc.UpsertMany(new[] { doc }, ct);
        return Ok(new { count = doc.Tags.Count, tags = doc.Tags });
    }

    [HttpPost("tags/censor/clear")]
    public async Task<IActionResult> ClearCensorTags(CancellationToken ct)
    {
        var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct);
        if (doc is null) return Ok(new { count = 0, tags = Array.Empty<string>() });
        doc.Tags = new List<string>();
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await Models.CensorTagsDoc.UpsertMany(new[] { doc }, ct);
        return Ok(new { count = 0, tags = Array.Empty<string>() });
    }

    public record RemoveCensorTagRequest(string? Tag);

    [HttpPost("tags/censor/remove")]
    public async Task<IActionResult> RemoveCensorTag([FromBody] RemoveCensorTagRequest req, CancellationToken ct)
    {
        var tag = (req?.Tag ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(tag)) return BadRequest(new { error = "tag is required" });
        var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct) ?? new Models.CensorTagsDoc { Id = "recs:censor-tags", Tags = new List<string>() };
        if (doc.Tags is null) doc.Tags = new List<string>();
        var before = doc.Tags.Count;
        doc.Tags = doc.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToList();
        if (doc.Tags.Count != before)
        {
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await Models.CensorTagsDoc.UpsertMany(new[] { doc }, ct);
        }
        return Ok(new { count = doc.Tags.Count, tags = doc.Tags.OrderBy(s => s).ToArray() });
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

    [HttpPost("genres/rebuild")] // on-demand genre catalog rebuild
    public async Task<IActionResult> RebuildGenres([FromServices] S5.Recs.Services.ISeedService seeder, CancellationToken ct)
    {
        var n = await seeder.RebuildGenreCatalogAsync(ct);
        return Ok(new { updated = n });
    }

    [HttpGet("tags/all")] // Admin-only endpoint to get ALL tags including censored ones
    public async Task<IActionResult> GetAllTags([FromQuery] string? sort = "popularity", CancellationToken ct = default)
    {
        var list = await Models.TagStatDoc.All(ct);
        IEnumerable<Models.TagStatDoc> q = list;
        if (string.Equals(sort, "alpha", StringComparison.OrdinalIgnoreCase) || string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
            q = q.OrderBy(t => t.Tag);
        else
            q = q.OrderByDescending(t => t.AnimeCount).ThenBy(t => t.Tag);
        return Ok(q.Select(t => new { tag = t.Tag, count = t.AnimeCount }));
    }

    [HttpGet("tags/censor/hashes")] // Generate MD5 hashes for preemptive filtering
    public async Task<IActionResult> GetCensoredTagHashes(CancellationToken ct)
    {
        var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct);
        var tags = doc?.Tags ?? new List<string>();

        var hashes = tags.Select(tag =>
        {
            var normalizedTag = tag.Trim().ToLowerInvariant();
            var bytes = System.Text.Encoding.UTF8.GetBytes(normalizedTag);
            var hash = System.Security.Cryptography.MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }).OrderBy(h => h).ToArray();

        return Ok(new {
            count = hashes.Length,
            hashes = hashes,
            generated = DateTimeOffset.UtcNow,
            note = "MD5 hashes of normalized (lowercase, trimmed) censored tags for preemptive filtering"
        });
    }

    [HttpGet("tags/preemptive-filter/status")] // Get preemptive filter diagnostics
    public IActionResult GetPreemptiveFilterStatus()
    {
        var hashCount = Infrastructure.PreemptiveTagFilter.PreemptiveHashCount;
        return Ok(new {
            enabled = hashCount > 0,
            preemptiveHashCount = hashCount,
            note = hashCount > 0
                ? "Preemptive filtering is active. Tags matching stored MD5 hashes will be auto-censored during import."
                : "Preemptive filtering is inactive. No hash list loaded. Update PreemptiveTagFilter.cs with hashes from /admin/tags/censor/hashes"
        });
    }

    public record TestPreemptiveFilterRequest(string Tag);

    [HttpPost("tags/preemptive-filter/test")] // Test if a specific tag would be preemptively filtered
    public IActionResult TestPreemptiveFilter([FromBody] TestPreemptiveFilterRequest req)
    {
        var tag = req?.Tag?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return BadRequest(new { error = "tag is required" });
        }

        var shouldCensor = Infrastructure.PreemptiveTagFilter.ShouldCensor(tag);
        var hash = Infrastructure.PreemptiveTagFilter.GetHashForTag(tag);

        return Ok(new {
            tag = tag,
            shouldCensor = shouldCensor,
            hash = hash,
            note = shouldCensor
                ? "This tag would be automatically censored during import"
                : "This tag would pass through the preemptive filter"
        });
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
            await HttpContext.Response.WriteAsync($"data: {Newtonsoft.Json.JsonConvert.SerializeObject(status)}\n\n", ct);
            await HttpContext.Response.Body.FlushAsync(ct);
            await Task.Delay(1000, ct);
        }
    }
}