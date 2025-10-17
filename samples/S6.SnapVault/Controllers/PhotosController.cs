using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Koan.Data.Core;
using Koan.Web.Controllers;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using S6.SnapVault.Hubs;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Photo management API - demonstrates batch operations, semantic search, and storage tiers
/// </summary>
[Route("api/[controller]")]
public class PhotosController : EntityController<PhotoAsset>
{
    private readonly ILogger<PhotosController> _logger;
    private readonly IPhotoProcessingService _processingService;
    private readonly IPhotoProcessingQueue _queue;
    private readonly IHubContext<PhotoProcessingHub> _hubContext;

    public PhotosController(
        ILogger<PhotosController> logger,
        IPhotoProcessingService processingService,
        IPhotoProcessingQueue queue,
        IHubContext<PhotoProcessingHub> hubContext)
    {
        _logger = logger;
        _processingService = processingService;
        _queue = queue;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Get all photos with pagination and sorting
    /// Overrides EntityController base GET to add pagination support
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PhotoAsset>>> GetPhotos(
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var allPhotos = await PhotoAsset.All(ct);

        // Apply sorting (support -id for descending)
        if (!string.IsNullOrEmpty(sort))
        {
            if (sort == "-id")
            {
                allPhotos = allPhotos.OrderByDescending(p => p.Id).ToList();
            }
            else if (sort == "id")
            {
                allPhotos = allPhotos.OrderBy(p => p.Id).ToList();
            }
        }

        // Apply pagination
        var paginatedPhotos = allPhotos
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        _logger.LogInformation(
            "GET /api/photos returned {Count} photos (page {Page}, pageSize {PageSize}, total {Total})",
            paginatedPhotos.Count, page, pageSize, allPhotos.Count);

        return Ok(paginatedPhotos);
    }

    /// <summary>
    /// Upload photos to an event (or auto-create daily album if eventId not provided)
    /// Demonstrates: File upload, batch processing, background jobs, auto-organization
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(104857600)] // 100MB limit
    public async Task<ActionResult<UploadResponse>> UploadPhotos(
        [FromForm] string? eventId,
        [FromForm] List<IFormFile> files,
        CancellationToken ct = default)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { Error = "No files provided" });
        }

        // If eventId provided, validate it exists
        Event? evt = null;
        if (!string.IsNullOrEmpty(eventId))
        {
            evt = await Event.Get(eventId, ct);
            if (evt == null)
            {
                return NotFound(new { Error = "Event not found" });
            }
        }

        // Validate files
        var validFiles = files.Where(f => IsValidImageFile(f)).ToList();
        if (validFiles.Count == 0)
        {
            return BadRequest(new { Error = "No valid image files found" });
        }

        // Create processing job
        var job = new ProcessingJob
        {
            EventId = eventId ?? "auto",
            TotalPhotos = validFiles.Count,
            Status = ProcessingStatus.InProgress
        };
        await job.Save(ct);

        _logger.LogInformation(
            "Created upload job {JobId} with {Count} file(s) for event {EventId}",
            job.Id, validFiles.Count, eventId ?? "auto");

        // Queue files for background processing
        var queuedCount = 0;
        foreach (var file in validFiles)
        {
            try
            {
                // Read file into memory for queuing
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                var fileData = ms.ToArray();

                // Queue for background processing
                var queuedUpload = new QueuedPhotoUpload
                {
                    JobId = job.Id,
                    EventId = eventId,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    FileData = fileData
                };

                _queue.Enqueue(queuedUpload);
                queuedCount++;

                // Notify clients that file is queued
                await _hubContext.Clients.Group($"job:{job.Id}").SendAsync("PhotoQueued", new PhotoProgressEvent
                {
                    JobId = job.Id,
                    PhotoId = "", // Not yet created
                    FileName = file.FileName,
                    Status = "queued",
                    Stage = "queued"
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue file {FileName}", file.FileName);
                job.Errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        // Update job with queue status
        job.ProcessedPhotos = 0; // Will be updated by background worker
        await job.Save(ct);

        _logger.LogInformation(
            "Queued {QueuedCount} of {TotalCount} file(s) for background processing (Job: {JobId})",
            queuedCount, validFiles.Count, job.Id);

        // Return immediately - processing happens in background
        return Ok(new UploadResponse
        {
            JobId = job.Id,
            TotalQueued = queuedCount,
            TotalFailed = job.Errors.Count,
            Message = $"Queued {queuedCount} photo(s) for processing. Connect to SignalR hub to receive real-time updates."
        });
    }

    /// <summary>
    /// Semantic search - demonstrates Koan.Data.Vector integration
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<SearchResponse>> SearchPhotos([FromBody] SearchRequest request)
    {
        if (string.IsNullOrEmpty(request.Query))
        {
            return BadRequest(new { Error = "Query is required" });
        }

        // Use service for semantic search with user-controlled alpha (with built-in fallback)
        var photos = await _processingService.SemanticSearchAsync(
            query: request.Query,
            eventId: request.EventId,
            alpha: request.Alpha,
            topK: request.Limit
        );

        return Ok(new SearchResponse
        {
            Photos = photos,
            Query = request.Query,
            ResultCount = photos.Count
        });
    }

    /// <summary>
    /// Get photos for an event with pagination
    /// </summary>
    [HttpGet("by-event/{eventId}")]
    public async Task<ActionResult<PaginatedResponse>> GetByEvent(
        string eventId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var allPhotos = await PhotoAsset.Query(p => p.EventId == eventId, ct);
        var totalCount = allPhotos.Count;

        var photos = allPhotos
            .OrderByDescending(p => p.CapturedAt ?? p.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new PaginatedResponse
        {
            Photos = photos,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Toggle favorite status
    /// </summary>
    [HttpPost("{id}/favorite")]
    public async Task<ActionResult> ToggleFavorite(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        photo.IsFavorite = !photo.IsFavorite;
        await photo.Save(ct);

        return Ok(new { IsFavorite = photo.IsFavorite });
    }

    /// <summary>
    /// Set photo rating (0-5 stars)
    /// </summary>
    [HttpPost("{id}/rate")]
    public async Task<ActionResult> RatePhoto(string id, [FromBody] RateRequest request, CancellationToken ct = default)
    {
        if (request.Rating < 0 || request.Rating > 5)
        {
            return BadRequest(new { Error = "Rating must be between 0 and 5" });
        }

        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        photo.Rating = request.Rating;
        await photo.Save(ct);

        return Ok(new { Rating = photo.Rating });
    }

    /// <summary>
    /// Regenerate AI description and embedding for a photo
    /// </summary>
    [HttpPost("{id}/regenerate-ai")]
    public async Task<ActionResult> RegenerateAI(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        _logger.LogInformation("Regenerating AI metadata for photo {PhotoId}", id);

        // Clear existing AI data
        photo.DetailedDescription = "";
        photo.Embedding = null;
        photo.AutoTags = new List<string>();
        photo.DetectedObjects = new List<string>();
        photo.MoodDescription = "";
        await photo.Save(ct);

        // Regenerate AI metadata in background
        _ = Task.Run(async () =>
        {
            try
            {
                await _processingService.GenerateAIMetadataAsync(photo, CancellationToken.None);
                _logger.LogInformation("Successfully regenerated AI metadata for photo {PhotoId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to regenerate AI metadata for photo {PhotoId}", id);
            }
        }, CancellationToken.None);

        return Ok(new { Message = "AI regeneration started in background", PhotoId = id });
    }

    /// <summary>
    /// Download full-resolution photo
    /// </summary>
    [HttpGet("{id}/download")]
    public ActionResult DownloadPhoto(string id)
    {
        var photo = PhotoAsset.Get(id);
        if (photo == null)
        {
            return NotFound();
        }

        // Redirect to the storage URL for the full-resolution photo
        return Redirect($"/storage/{photo.Key}");
    }

    /// <summary>
    /// Bulk delete photos
    /// </summary>
    [HttpPost("bulk/delete")]
    public async Task<ActionResult> BulkDelete([FromBody] BulkRequest request, CancellationToken ct = default)
    {
        if (request.PhotoIds == null || request.PhotoIds.Count == 0)
        {
            return BadRequest(new { Error = "PhotoIds is required" });
        }

        var deleted = 0;
        var errors = new List<string>();

        foreach (var id in request.PhotoIds)
        {
            try
            {
                var photo = await PhotoAsset.Get(id, ct);
                if (photo != null)
                {
                    await photo.Delete(ct);
                    deleted++;
                }
                else
                {
                    errors.Add($"Photo {id} not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete photo {PhotoId}", id);
                errors.Add($"Photo {id}: {ex.Message}");
            }
        }

        return Ok(new
        {
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors
        });
    }

    /// <summary>
    /// Get filter metadata (distinct values for filtering)
    /// </summary>
    [HttpGet("filter-metadata")]
    public async Task<ActionResult<FilterMetadata>> GetFilterMetadata(CancellationToken ct = default)
    {
        var allPhotos = await PhotoAsset.All(ct);

        var metadata = new FilterMetadata
        {
            CameraModels = allPhotos
                .Where(p => !string.IsNullOrEmpty(p.CameraModel))
                .Select(p => p.CameraModel!)
                .Distinct()
                .OrderBy(c => c)
                .ToList(),

            Years = allPhotos
                .Where(p => p.CapturedAt.HasValue)
                .Select(p => p.CapturedAt!.Value.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList(),

            Tags = allPhotos
                .SelectMany(p => p.AutoTags)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Take(50) // Top 50 tags
                .Select(g => new TagInfo { Tag = g.Key, Count = g.Count() })
                .ToList()
        };

        return Ok(metadata);
    }

    /// <summary>
    /// Bulk favorite/unfavorite photos
    /// </summary>
    [HttpPost("bulk/favorite")]
    public async Task<ActionResult> BulkFavorite([FromBody] BulkRequest request, CancellationToken ct = default)
    {
        if (request.PhotoIds == null || request.PhotoIds.Count == 0)
        {
            return BadRequest(new { Error = "PhotoIds is required" });
        }

        var updated = 0;
        var errors = new List<string>();

        foreach (var id in request.PhotoIds)
        {
            try
            {
                var photo = await PhotoAsset.Get(id, ct);
                if (photo != null)
                {
                    photo.IsFavorite = request.IsFavorite;
                    await photo.Save(ct);
                    updated++;
                }
                else
                {
                    errors.Add($"Photo {id} not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update favorite for photo {PhotoId}", id);
                errors.Add($"Photo {id}: {ex.Message}");
            }
        }

        return Ok(new
        {
            Updated = updated,
            Failed = errors.Count,
            Errors = errors,
            IsFavorite = request.IsFavorite
        });
    }

    private bool IsValidImageFile(IFormFile file)
    {
        var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".heic" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        return validExtensions.Contains(extension) && file.Length > 0 && file.Length <= 25 * 1024 * 1024; // 25MB max
    }
}

public class UploadResponse
{
    public string JobId { get; set; } = "";
    public int TotalQueued { get; set; }
    public int TotalFailed { get; set; }
    public string Message { get; set; } = "";
}

public class SearchRequest
{
    public string Query { get; set; } = "";
    public string? EventId { get; set; }
    public double Alpha { get; set; } = 0.5; // 0.0 = exact, 1.0 = semantic
    public int Limit { get; set; } = 20;
}

public class RateRequest
{
    public int Rating { get; set; }
}

public class SearchResponse
{
    public List<PhotoAsset> Photos { get; set; } = new();
    public string Query { get; set; } = "";
    public int ResultCount { get; set; }
    public bool UsedFallback { get; set; }
}

public class PaginatedResponse
{
    public List<PhotoAsset> Photos { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class BulkRequest
{
    public List<string> PhotoIds { get; set; } = new();
    public bool IsFavorite { get; set; }
}

public class FilterMetadata
{
    public List<string> CameraModels { get; set; } = new();
    public List<int> Years { get; set; } = new();
    public List<TagInfo> Tags { get; set; } = new();
}

public class TagInfo
{
    public string Tag { get; set; } = "";
    public int Count { get; set; }
}
