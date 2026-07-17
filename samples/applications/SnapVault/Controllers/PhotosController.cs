using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Initialization;
using SnapVault.Models;
using SnapVault.Services;

namespace SnapVault.Controllers;

/// <summary>
/// Studio photo API. <see cref="EntityController{PhotoAsset}"/> supplies ordinary reads; custom actions express
/// upload, navigation, facets, studio mutations, and durable reanalysis. Tenant and access axes apply throughout.
/// </summary>
[Route("api/photos")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 200, DefaultSort = "-id")]
[OperatorOnly]
public sealed class PhotosController : EntityController<PhotoAsset>
{
    private const int MaxFilesPerBatch = 10;
    private const long MaxFileBytes = 25L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private readonly PhotoSetService _photoSets;
    private readonly PhotoProcessingService _processing;

    public PhotosController(PhotoSetService photoSets, PhotoProcessingService processing)
    {
        _photoSets = photoSets;
        _processing = processing;
    }

    /// <summary>Library totals independent of the current page.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<PhotoStats>> GetStats(CancellationToken ct = default)
    {
        // Count pushdown (a 1-row page carrying the total) — no full materialization just to count.
        var total = await PhotoAsset.AllWithCount(QueryDefinition.All.WithPagination(1, 1), ct);
        var favorites = await PhotoAsset.QueryWithCount(p => p.IsFavorite, QueryDefinition.All.WithPagination(1, 1), ct);
        return Ok(new PhotoStats { TotalPhotos = (int)total.TotalCount, Favorites = (int)favorites.TotalCount });
    }

    /// <summary>A photo's position within the same sorted context used by the gallery.</summary>
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

    /// <summary>One event's photos, newest capture first and paginated.</summary>
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

    /// <summary>Distinct camera/year facets and the 50 most frequent tags.</summary>
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
    /// Streams each raw file to crash-safe staging, then submits one
    /// durable, tenant-carried <see cref="PhotoProcessingJob"/> per file sharing a <c>batchId</c>. Returns immediately
    /// with that batch id; the browser opens the SSE progress stream on it. The ambient studio tenant is captured at
    /// submission and restored when each job runs.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(260L * 1024 * 1024)]                                  // UI max batch = 10 files × 25 MB + headroom
    [RequestFormLimits(MultipartBodyLengthLimit = 260L * 1024 * 1024)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
        if (form.Files.Count == 0)
            return BadRequest(new { error = "No files provided." });
        if (form.Files.Count > MaxFilesPerBatch)
            return BadRequest(new { error = $"Upload at most {MaxFilesPerBatch} files per batch." });

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

        long totalQueued = 0;
        if (jobs.Count > 0)
        {
            var submission = await jobs.Submit(PhotoProcessingJob.Ingest, ct);
            totalQueued = submission.Accepted;
        }

        // Shape honored by upload.js: { jobId, totalQueued } (totalFailed is additive).
        return Ok(new { jobId = batchId, totalQueued, totalFailed = failed.Count });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Studio mutations remain thin actions over the entity: deletes
    // fire the structural AfterRemove cleanup (PhotoAssetCleanup), regeneration rides the durable tenant-carrying
    // job, and lock keys honour INV-1 (lowercase). Isolation is inherited from the ambient axes (no [Authorize]).
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Toggle favorite and return the new state.</summary>
    [HttpPost("{id}/favorite")]
    public async Task<IActionResult> ToggleFavorite(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();
        photo.IsFavorite = !photo.IsFavorite;
        await photo.Save(ct);
        return Ok(new { photo.IsFavorite });
    }

    /// <summary>Set a zero-to-five studio rating.</summary>
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

    /// <summary>Favorite or unfavorite a set; collect per-item failures.</summary>
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
    /// Bulk delete is the only delete path; raw EntityController write verbs are sealed. Each
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

    /// <summary>Download the full-resolution original as an attachment.</summary>
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

    /// <summary>Submit durable reanalysis for a photo.</summary>
    [HttpPost("{id}/regenerate-ai")]
    public async Task<IActionResult> RegenerateAI(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo is null) return NotFound();

        // A durable job (not a fire-and-forget Task.Run) so regeneration runs in this studio's rehydrated tenant
        // Jobs carries the studio context and survives a restart.
        await new PhotoProcessingJob { PhotoId = id }.Job.Submit(PhotoProcessingJob.Reanalyze);
        return Ok(new { message = "AI regeneration queued", photoId = id });
    }

    /// <summary>Regenerate analysis synchronously while preserving locked facts and summary.</summary>
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

    /// <summary>Toggle a lowercase fact key's lock.</summary>
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

    /// <summary>Toggle the summary lock.</summary>
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

    /// <summary>Lock the summary and every fact.</summary>
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

    /// <summary>Unlock the summary and every fact.</summary>
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
