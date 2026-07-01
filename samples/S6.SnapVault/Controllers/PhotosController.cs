using Koan.Jobs;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
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

    public PhotosController(PhotoSetService photoSets) => _photoSets = photoSets;

    /// <summary>#1 — library stats for the sidebar badges (accurate regardless of pagination).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<PhotoStats>> GetStats(CancellationToken ct = default)
    {
        var all = await PhotoAsset.All(ct);
        return Ok(new PhotoStats { TotalPhotos = all.Count, Favorites = all.Count(p => p.IsFavorite) });
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
}
