using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;
using S6.SnapVault.Services;

namespace S6.SnapVault.Controllers;

/// <summary>
/// The studio photo surface. An <see cref="EntityController{PhotoAsset}"/> so the framework provides the list (#2 —
/// filter/sort/pagination + <c>X-Total-Count</c>) and get-by-id (#3) for free (Reference = Intent); the custom
/// actions add upload (#8), stats (#1), index (#4), by-event (#6), and filter facets (#7). Isolation is inherited
/// from the ambient access + tenancy axes (no per-endpoint auth ceremony — see UploadProgressController); a studio
/// operator is unconstrained within their tenant.
/// </summary>
[Route("api/photos")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 200, DefaultSort = "-id")]
public sealed class PhotosController : EntityController<PhotoAsset>
{
    private const long MaxFileBytes = 25L * 1024 * 1024;                 // 25 MB per file
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".heic", ".heif" };

    private readonly PhotoSetService _photoSets;
    private readonly IPhotoProcessingService _processing;

    public PhotosController(PhotoSetService photoSets, IPhotoProcessingService processing)
    {
        _photoSets = photoSets;
        _processing = processing;
    }

    /// <summary>#1 — library stats for the sidebar badges (accurate regardless of pagination).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<PhotoStats>> GetStats(CancellationToken ct = default)
    {
        // Count pushdown (a 1-row page carrying the total) — no full materialization just to count.
        var total = await PhotoAsset.AllWithCount(QueryDefinition.All.WithPagination(1, 1), ct);
        var favorites = await PhotoAsset.QueryWithCount(p => p.IsFavorite, QueryDefinition.All.WithPagination(1, 1), ct);
        return Ok(new PhotoStats { TotalPhotos = (int)total.TotalCount, Favorites = (int)favorites.TotalCount });
    }

    /// <summary>#4 — a photo's position within a browsing context (lightbox jump-to-index). Reuses the same context
    /// routing/sort as the grid (#5) so index and grid never disagree. Defaults sort to <c>capturedAt</c> to match
    /// what the UI browses by.</summary>
    [HttpGet("{id}/index")]
    public async Task<ActionResult<PhotoIndexResponse>> GetPhotoIndex(
        string id,
        [FromQuery] string context = "all-photos",
        [FromQuery] string? collectionId = null,
        [FromQuery] string? searchQuery = null,
        [FromQuery] double searchAlpha = 0.5,
        [FromQuery] string sortBy = "capturedAt",
        [FromQuery] string sortOrder = "desc",
        CancellationToken ct = default)
    {
        var def = new PhotoSetDefinition
        {
            Context = context,
            CollectionId = collectionId,
            SearchQuery = searchQuery,
            SearchAlpha = searchAlpha,
            SortBy = sortBy,
            SortOrder = sortOrder,
        };
        var photos = await _photoSets.MaterializeContext(def, ct);
        var index = photos.FindIndex(p => p.Id == id);
        if (index < 0)
            return NotFound(new { error = "Photo not found in current context" });

        return Ok(new PhotoIndexResponse
        {
            Index = index,
            TotalCount = photos.Count,
            HasNext = index < photos.Count - 1,
            HasPrevious = index > 0,
        });
    }

    /// <summary>#6 — one event's photos (sidebar event click), newest-capture first, paginated.</summary>
    [HttpGet("by-event/{eventId}")]
    public async Task<ActionResult<EventPhotosResponse>> GetByEvent(
        string eventId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var all = await PhotoAsset.Query(p => p.EventId == eventId, ct);
        var photos = all
            .OrderByDescending(p => p.CapturedAt ?? p.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(PhotoMetadata.From)
            .ToList();

        return Ok(new EventPhotosResponse
        {
            Photos = photos,
            Page = page,
            PageSize = pageSize,
            TotalCount = all.Count,
            TotalPages = (int)Math.Ceiling(all.Count / (double)pageSize),
        });
    }

    /// <summary>#7 — filter facets: distinct cameras (alpha), years (desc), and the top-50 tags by frequency.</summary>
    [HttpGet("filter-metadata")]
    public async Task<ActionResult<FilterMetadata>> GetFilterMetadata(CancellationToken ct = default)
    {
        var all = await PhotoAsset.All(ct);
        return Ok(new FilterMetadata
        {
            CameraModels = all.Where(p => !string.IsNullOrEmpty(p.CameraModel)).Select(p => p.CameraModel!).Distinct().OrderBy(c => c).ToList(),
            Years = all.Where(p => p.CapturedAt.HasValue).Select(p => p.CapturedAt!.Value.Year).Distinct().OrderByDescending(y => y).ToList(),
            Tags = all.SelectMany(p => p.AutoTags).GroupBy(t => t).OrderByDescending(g => g.Count()).Take(50)
                      .Select(g => new TagInfo(g.Key, g.Count())).ToList(),
        });
    }

    /// <summary>
    /// #8 — Batch upload. Streams each raw file to crash-safe staging (never onto the job ledger), then submits one
    /// durable, tenant-carried <see cref="PhotoProcessingJob"/> per file sharing a <c>batchId</c>. Returns immediately
    /// with that batch id; the browser opens the SSE progress stream on it. The ambient studio tenant is captured at
    /// submit and rehydrated when each job runs (ARCH-0100).
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(260L * 1024 * 1024)]                                  // UI max batch = 10 files × 25 MB + headroom
    [RequestFormLimits(MultipartBodyLengthLimit = 260L * 1024 * 1024)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
        if (form.Files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        // "auto" (or omitted) means auto-organize by EXIF capture date; the pipeline mints the daily event.
        var eventIdRaw = form["eventId"].ToString();
        var eventId = string.IsNullOrEmpty(eventIdRaw) || eventIdRaw == "auto" ? null : eventIdRaw;

        var batchId = Guid.NewGuid().ToString("n");   // unguessable per-upload batch key (the SSE stream subscribes to it)
        var jobs = new List<PhotoProcessingJob>();
        var failed = new List<string>();

        foreach (var file in form.Files)
        {
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext) || file.Length <= 0 || file.Length > MaxFileBytes)
            {
                failed.Add(file.FileName);
                continue;
            }

            try
            {
                // Stage the raw bytes (tenant-prefixed, crash-safe) and enqueue the job that reads them back.
                await using var stream = file.OpenReadStream();
                var contentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
                var staged = await UploadStaging.Onboard(file.FileName, stream, contentType);
                jobs.Add(new PhotoProcessingJob
                {
                    EventId = eventId,
                    OriginalFileName = file.FileName,
                    ContentType = contentType,
                    StagingKey = staged.Key,
                    BatchJobId = batchId,
                });
            }
            catch (Exception)
            {
                failed.Add(file.FileName);
            }
        }

        if (jobs.Count > 0)
            await jobs.Submit(PhotoProcessingJob.Ingest, ct);

        // Shape honored by upload.js: { jobId, totalQueued } (totalFailed is additive).
        return Ok(new { jobId = batchId, totalQueued = jobs.Count, totalFailed = failed.Count });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Studio mutations (#9–#19). Thin actions over the entity: the interesting behaviour is elsewhere — deletes
    // fire the structural AfterRemove cleanup (PhotoAssetCleanup), regeneration rides the durable tenant-carrying
    // job, and lock keys honour INV-1 (lowercase). Isolation is inherited from the ambient axes (no [Authorize]).
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>#9 — toggle favorite (POST, no body). Returns the new state as { isFavorite }.</summary>
    [HttpPost("{id}/favorite")]
    public async Task<IActionResult> ToggleFavorite(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        photo.IsFavorite = !photo.IsFavorite;
        await photo.Save(ct);
        return Ok(new { photo.IsFavorite });
    }

    /// <summary>#10 — set rating (0..5). Returns { rating }.</summary>
    [HttpPost("{id}/rate")]
    public async Task<IActionResult> Rate(string id, [FromBody] RateRequest request, CancellationToken ct = default)
    {
        if (request is null || request.Rating < 0 || request.Rating > 5)
            return BadRequest(new { error = "Rating must be between 0 and 5." });

        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        photo.Rating = request.Rating;
        await photo.Save(ct);
        return Ok(new { photo.Rating });
    }

    /// <summary>#11 — bulk favorite/unfavorite. Per-item failures are collected, never fatal.</summary>
    [HttpPost("bulk/favorite")]
    public async Task<IActionResult> BulkFavorite([FromBody] BulkPhotoRequest request, CancellationToken ct = default)
    {
        if (request?.PhotoIds is null || request.PhotoIds.Count == 0)
            return BadRequest(new { error = "photoIds is required." });

        var updated = 0;
        var errors = new List<string>();
        foreach (var photoId in request.PhotoIds)
        {
            try
            {
                var photo = await PhotoAsset.Get(photoId, ct);
                if (photo is null) { errors.Add($"Photo {photoId} not found"); continue; }
                photo.IsFavorite = request.IsFavorite;
                await photo.Save(ct);
                updated++;
            }
            catch (Exception ex) { errors.Add($"Photo {photoId}: {ex.Message}"); }
        }

        return Ok(new { updated, failed = errors.Count, errors, request.IsFavorite });
    }

    /// <summary>
    /// #12 — bulk delete (the ONLY delete path; the raw EntityController delete verbs are sealed). Each
    /// <c>Remove</c> fires the structural AfterRemove hook (cached-render eviction + collection dead-id pruning),
    /// so the controller stays a thin loop. Returns { deleted, errors } — the SPA reads <c>deleted</c> and
    /// <c>errors[0]</c>.
    /// </summary>
    [HttpPost("bulk/delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkPhotoRequest request, CancellationToken ct = default)
    {
        if (request?.PhotoIds is null || request.PhotoIds.Count == 0)
            return BadRequest(new { error = "photoIds is required." });

        var deleted = 0;
        var errors = new List<string>();
        foreach (var photoId in request.PhotoIds)
        {
            try
            {
                var photo = await PhotoAsset.Get(photoId, ct);
                if (photo is null) { errors.Add($"Photo {photoId} not found"); continue; }
                await photo.Remove(ct);   // → AfterRemove: evict cached renders + prune collections
                deleted++;
            }
            catch (Exception ex) { errors.Add($"Photo {photoId}: {ex.Message}"); }
        }

        return Ok(new { deleted, failed = errors.Count, errors });
    }

    /// <summary>#13 — download the full-resolution original as an attachment (SPA opens it via window.open).</summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id, CancellationToken ct = default)
    {
        // Async data read (not the StorageEntity sync Get(key) proxy, which would shadow it and never 404).
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();

        var stream = await photo.OpenRead(ct);
        var fileName = string.IsNullOrEmpty(photo.OriginalFileName) ? $"{id}.jpg" : photo.OriginalFileName;
        var contentType = string.IsNullOrEmpty(photo.ContentType) ? "application/octet-stream" : photo.ContentType!;
        return File(stream, contentType, fileName);   // Content-Disposition: attachment
    }

    /// <summary>#14 — regenerate AI (fire-and-forget). Submits the durable, tenant-carrying Reanalyze job.</summary>
    [HttpPost("{id}/regenerate-ai")]
    public async Task<IActionResult> RegenerateAI(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();

        // A durable job (not a fire-and-forget Task.Run) so regeneration runs in this studio's rehydrated tenant
        // (ARCH-0100) and survives a restart. The Reanalyze handler calls RegenerateAIAnalysis (reroll-with-holds).
        await new PhotoProcessingJob { PhotoId = id }.Job.Submit(PhotoProcessingJob.Reanalyze);
        return Ok(new { message = "AI regeneration queued", photoId = id });
    }

    /// <summary>#15 — regenerate AI analysis synchronously, preserving locked facts + summary ("reroll with holds").</summary>
    [HttpPost("{id}/regenerate-ai-analysis")]
    public async Task<IActionResult> RegenerateAIAnalysis(string id, [FromBody] RegenerateAIAnalysisRequest? request = null, CancellationToken ct = default)
    {
        try
        {
            var photo = await _processing.RegenerateAIAnalysis(id, request?.AnalysisStyleId, ct);
            return Ok(photo);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>#16 — toggle a fact's lock. INV-1: fact keys are lowercase. Returns { isLocked, lockedFactKeys }.</summary>
    [HttpPost("{id}/facts/{factKey}/toggle-lock")]
    public async Task<IActionResult> ToggleFactLock(string id, string factKey, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        if (photo.AiAnalysis is null) return BadRequest(new { error = "Photo has no AI analysis." });

        var key = factKey.ToLowerInvariant();   // INV-1 — facts are stored lowercase
        if (!photo.AiAnalysis.Facts.ContainsKey(key))
            return BadRequest(new { error = $"Fact key '{key}' not found in analysis." });

        bool isLocked;
        if (photo.AiAnalysis.LockedFactKeys.Contains(key)) { photo.AiAnalysis.LockedFactKeys.Remove(key); isLocked = false; }
        else { photo.AiAnalysis.LockedFactKeys.Add(key); isLocked = true; }
        await photo.Save(ct);

        return Ok(new { factKey = key, isLocked, lockedFactKeys = photo.AiAnalysis.LockedFactKeys.ToList() });
    }

    /// <summary>#17 — toggle the summary lock. Returns { summaryLocked }.</summary>
    [HttpPost("{id}/summary/toggle-lock")]
    public async Task<IActionResult> ToggleSummaryLock(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        if (photo.AiAnalysis is null) return BadRequest(new { error = "Photo has no AI analysis." });

        photo.AiAnalysis.SummaryLocked = !photo.AiAnalysis.SummaryLocked;
        await photo.Save(ct);
        return Ok(new { photo.AiAnalysis.SummaryLocked });
    }

    /// <summary>#18 — lock the summary and every fact.</summary>
    [HttpPost("{id}/facts/lock-all")]
    public async Task<IActionResult> LockAllFacts(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        if (photo.AiAnalysis is null) return BadRequest(new { error = "Photo has no AI analysis." });

        photo.AiAnalysis.SummaryLocked = true;
        photo.AiAnalysis.LockedFactKeys = new HashSet<string>(photo.AiAnalysis.Facts.Keys);   // keys already lowercase
        await photo.Save(ct);

        return Ok(new
        {
            summaryLocked = photo.AiAnalysis.SummaryLocked,
            lockedCount = photo.AiAnalysis.LockedFactKeys.Count,
            lockedFactKeys = photo.AiAnalysis.LockedFactKeys.ToList()
        });
    }

    /// <summary>#19 — unlock the summary and every fact.</summary>
    [HttpPost("{id}/facts/unlock-all")]
    public async Task<IActionResult> UnlockAllFacts(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        if (photo.AiAnalysis is null) return BadRequest(new { error = "Photo has no AI analysis." });

        photo.AiAnalysis.SummaryLocked = false;
        photo.AiAnalysis.LockedFactKeys.Clear();
        await photo.Save(ct);

        return Ok(new { summaryLocked = photo.AiAnalysis.SummaryLocked, lockedFactKeys = photo.AiAnalysis.LockedFactKeys.ToList() });
    }

    // ------------------------------------------------------------------------------------------------------------
    // §9.7 tripwire — seal the raw EntityController write/delete verbs (405). Photos enter ONLY via /upload (the
    // ingest pipeline that stores the original + extracts EXIF/AI); a raw Upsert would mint a blob-less PhotoAsset.
    // Photos leave ONLY via bulk/delete (which fires the AfterRemove cleanup). These overrides inherit the base
    // route attributes ([HttpPost("")], [HttpPost("bulk")], [HttpDelete("{id}")], …), so the routes still exist —
    // they just refuse.
    // ------------------------------------------------------------------------------------------------------------

    private static IActionResult SealedWrite() => new ObjectResult(new
    {
        error = "Photos are created via POST /api/photos/upload and removed via POST /api/photos/bulk/delete."
    })
    { StatusCode = StatusCodes.Status405MethodNotAllowed };

    public override Task<IActionResult> Upsert(PhotoAsset model, CancellationToken ct) => Task.FromResult(SealedWrite());
    public override Task<IActionResult> UpsertMany(IEnumerable<PhotoAsset> models, CancellationToken ct) => Task.FromResult(SealedWrite());
    public override Task<IActionResult> Patch(string id, CancellationToken ct) => Task.FromResult(SealedWrite());   // partial update would let EventId (the access axis) be rewritten
    public override Task<IActionResult> Delete(string id, CancellationToken ct) => Task.FromResult(SealedWrite());
    public override Task<IActionResult> DeleteMany(IEnumerable<string> ids, CancellationToken ct) => Task.FromResult(SealedWrite());
    public override Task<IActionResult> DeleteByQuery(string? q, CancellationToken ct) => Task.FromResult(SealedWrite());
    public override Task<IActionResult> DeleteAll(CancellationToken ct) => Task.FromResult(SealedWrite());
}
