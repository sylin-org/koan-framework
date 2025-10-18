using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using S6.SnapVault.Hubs;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Photo management API - demonstrates batch operations, semantic search, and storage tiers
/// </summary>
[Route("api/[controller]")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 200, DefaultSort = "-id")]
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
    /// Upload photos to an event (or auto-create daily album if eventId not provided)
    /// Demonstrates: File upload, batch processing, background jobs, auto-organization
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(524288000)] // 500MB limit for batch uploads
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
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
    /// DEPRECATED: Use /regenerate-ai-analysis instead for better lock support
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
    /// Regenerate AI analysis for a photo while preserving locked facts
    /// "Reroll with holds" - locked facts are preserved during regeneration
    /// </summary>
    [HttpPost("{id}/regenerate-ai-analysis")]
    public async Task<ActionResult<PhotoAsset>> RegenerateAIAnalysis(string id, CancellationToken ct = default)
    {
        try
        {
            var photo = await _processingService.RegenerateAIAnalysisAsync(id, ct);
            return Ok(photo);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate AI analysis for photo {PhotoId}", id);
            return StatusCode(500, new { Error = "Failed to regenerate AI analysis", Details = ex.Message });
        }
    }

    /// <summary>
    /// Toggle lock state of a specific fact in the AI analysis
    /// Returns the updated list of locked facts to support multi-tab scenarios
    /// </summary>
    [HttpPost("{id}/facts/{factKey}/toggle-lock")]
    public async Task<ActionResult> ToggleLockFact(string id, string factKey, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        if (photo.AiAnalysis == null)
        {
            return BadRequest(new { Error = "Photo has no AI analysis" });
        }

        // Find the actual fact key using case-insensitive comparison
        var actualFactKey = photo.AiAnalysis.Facts.Keys
            .FirstOrDefault(k => k.Equals(factKey, StringComparison.OrdinalIgnoreCase));

        if (actualFactKey == null)
        {
            return BadRequest(new { Error = $"Fact key '{factKey}' not found in analysis" });
        }

        // Normalize to lowercase for storage (all fact names in LockedFactKeys are lowercase)
        var normalizedKey = actualFactKey.ToLowerInvariant();

        // Toggle lock state
        bool isNowLocked;
        if (photo.AiAnalysis.LockedFactKeys.Contains(normalizedKey))
        {
            photo.AiAnalysis.LockedFactKeys.Remove(normalizedKey);
            isNowLocked = false;
            _logger.LogInformation("Unlocked fact {FactKey} for photo {PhotoId}", normalizedKey, id);
        }
        else
        {
            photo.AiAnalysis.LockedFactKeys.Add(normalizedKey);
            isNowLocked = true;
            _logger.LogInformation("Locked fact {FactKey} for photo {PhotoId}", normalizedKey, id);
        }

        await photo.Save(ct);

        return Ok(new
        {
            FactKey = normalizedKey,
            IsLocked = isNowLocked,
            LockedFactKeys = photo.AiAnalysis.LockedFactKeys.ToList()
        });
    }

    /// <summary>
    /// Lock all facts in the AI analysis
    /// </summary>
    [HttpPost("{id}/facts/lock-all")]
    public async Task<ActionResult> LockAllFacts(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        if (photo.AiAnalysis == null)
        {
            return BadRequest(new { Error = "Photo has no AI analysis" });
        }

        // Lock all existing fact keys (normalize to lowercase)
        photo.AiAnalysis.LockedFactKeys = new HashSet<string>(
            photo.AiAnalysis.Facts.Keys.Select(k => k.ToLowerInvariant())
        );
        await photo.Save(ct);

        _logger.LogInformation("Locked all {Count} facts for photo {PhotoId}", photo.AiAnalysis.LockedFactKeys.Count, id);

        return Ok(new
        {
            LockedCount = photo.AiAnalysis.LockedFactKeys.Count,
            LockedFactKeys = photo.AiAnalysis.LockedFactKeys
        });
    }

    /// <summary>
    /// Unlock all facts in the AI analysis
    /// </summary>
    [HttpPost("{id}/facts/unlock-all")]
    public async Task<ActionResult> UnlockAllFacts(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        if (photo.AiAnalysis == null)
        {
            return BadRequest(new { Error = "Photo has no AI analysis" });
        }

        var previousCount = photo.AiAnalysis.LockedFactKeys.Count;
        photo.AiAnalysis.LockedFactKeys.Clear();
        await photo.Save(ct);

        _logger.LogInformation("Unlocked all {Count} facts for photo {PhotoId}", previousCount, id);

        return Ok(new
        {
            UnlockedCount = previousCount,
            LockedFactKeys = photo.AiAnalysis.LockedFactKeys
        });
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
    /// Get Smart Collections for Discovery Panel
    /// Returns intelligent photo groupings with counts and preview thumbnails
    /// </summary>
    [HttpGet("smart-collections")]
    public async Task<ActionResult<SmartCollectionsResponse>> GetSmartCollections(CancellationToken ct = default)
    {
        var allPhotos = await PhotoAsset.All(ct);
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);

        var collections = new List<SmartCollection>();

        // 1. Recent Uploads
        var recentUploads = allPhotos
            .Where(p => p.UploadedAt >= sevenDaysAgo)
            .OrderByDescending(p => p.UploadedAt)
            .ToList();

        if (recentUploads.Any())
        {
            collections.Add(new SmartCollection
            {
                Id = "recent-uploads",
                Name = "Recent Uploads",
                Type = "system",
                PhotoCount = recentUploads.Count,
                Thumbnails = recentUploads.Take(4).Select(p => $"/storage/{p.MasonryThumbnailMediaId}").ToList(),
                LastUpdated = recentUploads.First().UploadedAt,
                Icon = "camera"
            });
        }

        // 2. Needs Attention (unrated or untagged photos older than 30 days)
        var needsAttention = allPhotos
            .Where(p => p.UploadedAt < thirtyDaysAgo && (p.Rating == 0 || p.AutoTags.Count == 0))
            .OrderByDescending(p => p.UploadedAt)
            .ToList();

        if (needsAttention.Any())
        {
            collections.Add(new SmartCollection
            {
                Id = "needs-attention",
                Name = "Needs Attention",
                Type = "system",
                PhotoCount = needsAttention.Count,
                Thumbnails = needsAttention.Take(4).Select(p => $"/storage/{p.MasonryThumbnailMediaId}").ToList(),
                LastUpdated = needsAttention.First().UploadedAt,
                Icon = "alert-circle",
                Description = "Unrated or untagged"
            });
        }

        // 3. This Week's Best (4-5 star photos from last 7 days)
        var thisWeeksBest = allPhotos
            .Where(p => p.Rating >= 4 && p.UploadedAt >= sevenDaysAgo)
            .OrderByDescending(p => p.Rating)
            .ThenByDescending(p => p.UploadedAt)
            .ToList();

        if (thisWeeksBest.Any())
        {
            collections.Add(new SmartCollection
            {
                Id = "this-weeks-best",
                Name = "This Week's Best",
                Type = "system",
                PhotoCount = thisWeeksBest.Count,
                Thumbnails = thisWeeksBest.Take(4).Select(p => $"/storage/{p.MasonryThumbnailMediaId}").ToList(),
                LastUpdated = thisWeeksBest.First().UploadedAt,
                Icon = "star",
                Description = "4-5 star ratings"
            });
        }
        else
        {
            // Fallback: Last Month's Best
            var lastMonthsBest = allPhotos
                .Where(p => p.Rating >= 4 && p.UploadedAt >= thirtyDaysAgo)
                .OrderByDescending(p => p.Rating)
                .ThenByDescending(p => p.UploadedAt)
                .ToList();

            if (lastMonthsBest.Any())
            {
                collections.Add(new SmartCollection
                {
                    Id = "last-months-best",
                    Name = "Last Month's Best",
                    Type = "system",
                    PhotoCount = lastMonthsBest.Count,
                    Thumbnails = lastMonthsBest.Take(4).Select(p => $"/storage/{p.MasonryThumbnailMediaId}").ToList(),
                    LastUpdated = lastMonthsBest.First().UploadedAt,
                    Icon = "star",
                    Description = "4-5 star ratings"
                });
            }
        }

        // 4. Camera Profiles (grouped by camera model)
        var cameraGroups = allPhotos
            .Where(p => !string.IsNullOrEmpty(p.CameraModel))
            .GroupBy(p => p.CameraModel)
            .OrderByDescending(g => g.Count())
            .Take(5) // Top 5 cameras
            .ToList();

        foreach (var group in cameraGroups)
        {
            var cameraPhotos = group.OrderByDescending(p => p.UploadedAt).ToList();
            collections.Add(new SmartCollection
            {
                Id = $"camera-{group.Key?.Replace(" ", "-").ToLower()}",
                Name = group.Key ?? "Unknown Camera",
                Type = "camera",
                PhotoCount = group.Count(),
                Thumbnails = cameraPhotos.Take(4).Select(p => $"/storage/{p.MasonryThumbnailMediaId}").ToList(),
                LastUpdated = cameraPhotos.First().UploadedAt,
                Icon = "camera-slr"
            });
        }

        // 5. Favorites
        var favorites = allPhotos
            .Where(p => p.IsFavorite)
            .OrderByDescending(p => p.UploadedAt)
            .ToList();

        if (favorites.Any())
        {
            collections.Add(new SmartCollection
            {
                Id = "favorites",
                Name = "Favorites",
                Type = "system",
                PhotoCount = favorites.Count,
                Thumbnails = favorites.Take(4).Select(p => $"/storage/{p.MasonryThumbnailMediaId}").ToList(),
                LastUpdated = favorites.First().UploadedAt,
                Icon = "heart"
            });
        }

        return Ok(new SmartCollectionsResponse
        {
            Collections = collections,
            TotalPhotos = allPhotos.Count
        });
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

public class SmartCollectionsResponse
{
    public List<SmartCollection> Collections { get; set; } = new();
    public int TotalPhotos { get; set; }
}

public class SmartCollection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "system", "camera", "ai", "custom"
    public int PhotoCount { get; set; }
    public List<string> Thumbnails { get; set; } = new(); // Up to 4 thumbnail URLs
    public DateTime LastUpdated { get; set; }
    public string Icon { get; set; } = ""; // Icon name for frontend
    public string? Description { get; set; } // Optional subtitle
}
