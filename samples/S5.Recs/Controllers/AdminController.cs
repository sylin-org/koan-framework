using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using S5.Recs.Infrastructure;
using S5.Recs.Services;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Admin)] // Koan guideline: controllers define routes
public class AdminController(ISeedService seeder, ILogger<AdminController> _logger, IEnumerable<Providers.IMediaProvider> providers) : ControllerBase
{

    [HttpPost("seed/start")]
    public IActionResult StartSeed([FromBody] SeedRequest req)
    {
        try
        {
            // Require MediaType to be explicitly provided
            if (string.IsNullOrWhiteSpace(req.MediaType))
            {
                return BadRequest(new { error = "MediaType is required. Please specify a media type or 'all' to import all types." });
            }

            var id = seeder.StartAsync(req.Source, req.MediaType, req.Limit, req.Overwrite, HttpContext.RequestAborted).GetAwaiter().GetResult();
            return Ok(new { jobId = id });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("import is already in progress"))
        {
            return Conflict(new { error = ex.Message, isImportInProgress = seeder.IsImportInProgress });
        }
    }

    [HttpGet("seed/status")]
    public IActionResult GetImportStatus()
    {
        return Ok(new { isImportInProgress = seeder.IsImportInProgress });
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
        var (media, contentPieces, vectors) = seeder.GetStatsAsync(HttpContext.RequestAborted).GetAwaiter().GetResult();
        return Ok(new { media, contentPieces, vectors });
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
            q = q.OrderByDescending(t => t.MediaCount).ThenBy(t => t.Tag);
        return Ok(q.Select(t => new { tag = t.Tag, count = t.MediaCount }));
    }

    [HttpGet("tags/debug")] // Debug endpoint to analyze tag data issues
    public async Task<IActionResult> DebugTagData(CancellationToken ct)
    {
        var allMedia = await Models.Media.All(ct);
        var mediaCount = allMedia.Count();

        var mediaWithTags = allMedia.Where(m => m.Tags != null && m.Tags.Length > 0).ToList();
        var mediaWithTagsCount = mediaWithTags.Count;

        // Extract all raw tags before filtering
        var allRawTags = new List<string>();
        foreach (var m in allMedia)
        {
            if (m.Genres is { Length: > 0 })
                allRawTags.AddRange(m.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()));
            if (m.Tags is { Length: > 0 })
                allRawTags.AddRange(m.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        }

        var uniqueRawTags = allRawTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Check preemptive filtering impact
        var flaggedByPreemptive = uniqueRawTags.Where(Infrastructure.PreemptiveTagFilter.ShouldCensor).ToList();
        var passedPreemptive = uniqueRawTags.Where(t => !Infrastructure.PreemptiveTagFilter.ShouldCensor(t)).ToList();

        // Sample some tags that have/don't have data
        var sampleMediaWithTags = mediaWithTags.Take(3).Select(m => new {
            id = m.Id,
            title = m.TitleEnglish ?? m.Title,
            genreCount = m.Genres?.Length ?? 0,
            tagCount = m.Tags?.Length ?? 0,
            genres = m.Genres?.Take(5),
            tags = m.Tags?.Take(5)
        }).ToList();

        return Ok(new {
            totalMedia = mediaCount,
            mediaWithTags = mediaWithTagsCount,
            mediaWithTagsPercent = mediaCount > 0 ? Math.Round((double)mediaWithTagsCount / mediaCount * 100, 1) : 0,
            totalRawTags = allRawTags.Count,
            uniqueRawTags = uniqueRawTags.Count,
            preemptiveFilterStats = new {
                flaggedCount = flaggedByPreemptive.Count,
                passedCount = passedPreemptive.Count,
                flaggedPercent = uniqueRawTags.Count > 0 ? Math.Round((double)flaggedByPreemptive.Count / uniqueRawTags.Count * 100, 1) : 0,
                sampleFlagged = flaggedByPreemptive.Take(10).ToList(),
                samplePassed = passedPreemptive.Take(10).ToList()
            },
            sampleMediaWithTags,
            currentTagStatCount = (await Models.TagStatDoc.All(ct)).Count()
        });
    }

    [HttpGet("tags/counts")] // Compare tag counts between admin and public endpoints
    public async Task<IActionResult> CompareTagCounts([FromServices] IOptions<S5.Recs.Options.TagCatalogOptions>? tagOptions, CancellationToken ct)
    {
        // Get all TagStatDoc records (what rebuild creates)
        var allTagStats = await Models.TagStatDoc.All(ct);
        var totalTagStatDocs = allTagStats.Count();

        // Apply the same filtering logic as /api/tags
        var opt = tagOptions?.Value?.CensorTags ?? Array.Empty<string>();
        var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct);
        var dyn = doc?.Tags?.ToArray() ?? Array.Empty<string>();
        var censor = opt.Concat(dyn).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var filteredTags = allTagStats.Where(t => !IsCensoredTag(t.Tag, censor)).ToList();
        var publicTagCount = filteredTags.Count();

        return Ok(new {
            totalTagStatDocs = totalTagStatDocs,      // What rebuild creates
            publicTagCount = publicTagCount,          // What /api/tags shows
            filteredOut = totalTagStatDocs - publicTagCount,
            censorRulesCount = censor.Length,
            sampleCensorRules = censor.Take(10).ToArray(),
            sampleFilteredOutTags = allTagStats
                .Where(t => IsCensoredTag(t.Tag, censor))
                .Take(10)
                .Select(t => new { tag = t.Tag, count = t.MediaCount })
                .ToArray()
        });
    }

    private static bool IsCensoredTag(string tag, string[]? censor)
        => !string.IsNullOrWhiteSpace(tag) &&
           (censor?.Any(c => !string.IsNullOrWhiteSpace(c) && tag.Equals(c, StringComparison.OrdinalIgnoreCase)) ?? false);

    [HttpGet("media/count")] // Quick media count check
    public async Task<IActionResult> GetMediaCount(CancellationToken ct)
    {
        var allMedia = await Models.Media.All(ct);
        var count = allMedia.Count();

        // Sample a few media to check for tag data
        var mediaWithTags = allMedia.Where(m => m.Tags != null && m.Tags.Length > 0).Take(10).ToList();
        var mediaWithGenres = allMedia.Where(m => m.Genres != null && m.Genres.Length > 0).Take(10).ToList();

        return Ok(new {
            totalMediaCount = count,
            sampleMediaWithTags = mediaWithTags.Select(m => new {
                id = m.Id,
                title = m.TitleEnglish ?? m.Title,
                tagCount = m.Tags?.Length ?? 0,
                genreCount = m.Genres?.Length ?? 0,
                sampleTags = m.Tags?.Take(3).ToArray(),
                sampleGenres = m.Genres?.Take(3).ToArray()
            }).ToArray(),
            sampleMediaWithGenres = mediaWithGenres.Select(m => new {
                id = m.Id,
                title = m.TitleEnglish ?? m.Title,
                tagCount = m.Tags?.Length ?? 0,
                genreCount = m.Genres?.Length ?? 0
            }).ToArray(),
            mediaWithTagsCount = allMedia.Count(m => m.Tags != null && m.Tags.Length > 0),
            mediaWithGenresCount = allMedia.Count(m => m.Genres != null && m.Genres.Length > 0)
        });
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
        var all = Models.Media.All(HttpContext.RequestAborted).Result.ToList();

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

    // Cache management endpoints

    [HttpGet("cache/list")]
    public async Task<IActionResult> ListCaches([FromServices] Services.IRawCacheService cache, CancellationToken ct)
    {
        var manifests = await cache.ListCachesAsync(ct);
        return Ok(new { count = manifests.Count, caches = manifests });
    }

    public record RebuildFromCacheRequest(string Source, string MediaType, string? JobId = null);

    [HttpPost("rebuild-db-from-cache")]
    public async Task<IActionResult> RebuildFromCache(
        [FromBody] RebuildFromCacheRequest req,
        [FromServices] Services.IRawCacheService cache,
        [FromServices] Services.IMediaParserRegistry parserRegistry,
        CancellationToken ct)
    {
        // Get parser for this source
        var parser = parserRegistry.GetParser(req.Source);
        if (parser == null)
        {
            return BadRequest(new { error = $"No parser found for source '{req.Source}'" });
        }

        // Resolve MediaType
        var mediaType = await Models.MediaType.All(ct)
            .ContinueWith(t => t.Result.FirstOrDefault(mt => mt.Name.Equals(req.MediaType, StringComparison.OrdinalIgnoreCase)), ct);

        if (mediaType == null)
        {
            return BadRequest(new { error = $"MediaType '{req.MediaType}' not found" });
        }

        // Determine which jobs to process
        List<Services.CacheManifest> jobsToProcess;
        if (!string.IsNullOrWhiteSpace(req.JobId))
        {
            // Process single specific job
            var manifest = await cache.GetManifestAsync(req.Source, req.MediaType, req.JobId, ct);
            if (manifest == null)
            {
                return NotFound(new { error = $"Cache not found: {req.Source}/{req.MediaType}/{req.JobId}" });
            }
            jobsToProcess = new List<Services.CacheManifest> { manifest };
        }
        else
        {
            // Process ALL jobs for this source/mediaType in chronological order (oldest first)
            var allManifests = await cache.ListCachesAsync(ct);
            jobsToProcess = allManifests
                .Where(m => m.Source.Equals(req.Source, StringComparison.OrdinalIgnoreCase) &&
                           m.MediaType.Equals(req.MediaType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.FetchedAt) // OLDEST FIRST to preserve update order
                .ToList();

            if (jobsToProcess.Count == 0)
            {
                return NotFound(new { error = $"No caches found for {req.Source}/{req.MediaType}" });
            }

            _logger.LogInformation("Rebuild: processing {Count} cache jobs in chronological order for {Source}/{MediaType}",
                jobsToProcess.Count, req.Source, req.MediaType);
        }

        // Parse and import per page to avoid memory issues
        int totalImported = 0;
        int totalCached = 0;

        foreach (var job in jobsToProcess)
        {
            _logger.LogInformation("Rebuild: processing job {JobId} (fetched {FetchedAt:u})",
                job.JobId, job.FetchedAt);

            await foreach (var (pageNum, rawJson) in cache.ReadPagesAsync(job.Source, job.MediaType, job.JobId, ct))
            {
                var parsedMedia = await parser.ParsePageAsync(rawJson, mediaType, ct);

                if (parsedMedia.Count > 0)
                {
                    var imported = await Models.Media.UpsertMany(parsedMedia, ct);
                    totalImported += imported;

                    if (pageNum % 50 == 0) // Log every 50 pages
                    {
                        _logger.LogInformation("Rebuild: page {Page} complete ({PageItems} items, total: {Total})",
                            pageNum, parsedMedia.Count, totalImported);
                    }
                }
            }

            totalCached += job.TotalItems;
        }

        _logger.LogInformation("Rebuilt database from {JobCount} cache job(s): {Source}/{MediaType} - imported {Imported} items",
            jobsToProcess.Count, req.Source, req.MediaType, totalImported);

        return Ok(new {
            source = req.Source,
            mediaType = req.MediaType,
            jobsProcessed = jobsToProcess.Count,
            jobIds = jobsToProcess.Select(j => j.JobId).ToArray(),
            imported = totalImported,
            cached = totalCached
        });
    }

    // Flush endpoints

    [HttpPost("flush/cache")]
    public async Task<IActionResult> FlushCache([FromServices] Services.IRawCacheService cache, CancellationToken ct)
    {
        var count = await cache.FlushAllAsync(ct);
        return Ok(new { flushed = "cache", count });
    }

    [HttpPost("flush/vectors")]
    public async Task<IActionResult> FlushVectors(CancellationToken ct)
    {
        // Delete all vector data
        var count = 0;
        if (Koan.Data.Vector.Vector<Models.Media>.IsAvailable)
        {
            await foreach (var media in Models.Media.AllStream(1000, ct))
            {
                await Koan.Data.Vector.Vector<Models.Media>.Delete(media.Id!, ct);
                count++;
            }
        }
        return Ok(new { flushed = "vectors", count });
    }

    [HttpPost("flush/tags")]
    public async Task<IActionResult> FlushTags(CancellationToken ct)
    {
        var allTags = await Models.TagStatDoc.All(ct);
        var count = allTags.Count();

        foreach (var tag in allTags)
        {
            await Models.TagStatDoc.Remove(tag.Id!, ct);
        }

        return Ok(new { flushed = "tags", count });
    }

    [HttpPost("flush/genres")]
    public async Task<IActionResult> FlushGenres(CancellationToken ct)
    {
        var allGenres = await Models.GenreStatDoc.All(ct);
        var count = allGenres.Count();

        foreach (var genre in allGenres)
        {
            await Models.GenreStatDoc.Remove(genre.Id!, ct);
        }

        return Ok(new { flushed = "genres", count });
    }

    [HttpPost("flush/media")]
    public async Task<IActionResult> FlushMedia(CancellationToken ct)
    {
        var count = 0;
        await foreach (var media in Models.Media.AllStream(1000, ct))
        {
            await Models.Media.Remove(media.Id!, ct);
            count++;
        }

        return Ok(new { flushed = "media", count });
    }
}