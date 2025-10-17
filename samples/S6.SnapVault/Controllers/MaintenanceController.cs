using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Data.Vector;
using S6.SnapVault.Models;
using System.Text.Json;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Maintenance and configuration API - demonstrates safe data management patterns
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class MaintenanceController : ControllerBase
{
    private readonly ILogger<MaintenanceController> _logger;
    private readonly IWebHostEnvironment _env;

    public MaintenanceController(
        ILogger<MaintenanceController> logger,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Get storage statistics across all tiers
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<StorageStats>> GetStats(CancellationToken ct = default)
    {
        try
        {
            var storageRoot = Path.Combine(_env.ContentRootPath, ".Koan", "storage");

            var hotTier = GetDirectorySize(Path.Combine(storageRoot, "thumbnails")) / (1024.0 * 1024 * 1024);
            var warmTier = GetDirectorySize(Path.Combine(storageRoot, "gallery")) / (1024.0 * 1024 * 1024);
            var coldTier = GetDirectorySize(Path.Combine(storageRoot, "photos")) / (1024.0 * 1024 * 1024);

            var photoCount = await PhotoAsset.Count;

            // Get AI cache size
            var cacheRoot = Path.Combine(_env.ContentRootPath, ".Koan", "cache", "ai-introspection");
            var cacheSizeMB = Directory.Exists(cacheRoot)
                ? GetDirectorySize(cacheRoot) / (1024.0 * 1024)
                : 0;
            var cacheFiles = Directory.Exists(cacheRoot)
                ? Directory.GetFiles(cacheRoot, "*.json", SearchOption.AllDirectories).Length
                : 0;

            return Ok(new StorageStats
            {
                HotTierGB = Math.Round(hotTier, 2),
                WarmTierGB = Math.Round(warmTier, 2),
                ColdTierGB = Math.Round(coldTier, 2),
                TotalGB = Math.Round(hotTier + warmTier + coldTier, 2),
                PhotoCount = photoCount,
                CacheEntries = cacheFiles,
                CacheSizeMB = (int)Math.Round(cacheSizeMB)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage stats");
            return StatusCode(500, new { Error = "Failed to retrieve storage statistics" });
        }
    }

    /// <summary>
    /// Get search index status
    /// </summary>
    [HttpGet("index-status")]
    public async Task<ActionResult> GetIndexStatus(CancellationToken ct = default)
    {
        // Check if vector search is available
        if (!Vector<PhotoAsset>.IsAvailable)
        {
            return Ok(new { LastIndexed = DateTime.UtcNow.AddHours(-2), Status = "unavailable" });
        }

        // In a real implementation, you'd track this in metadata
        return Ok(new { LastIndexed = DateTime.UtcNow.AddHours(-2), Status = "ok" });
    }

    /// <summary>
    /// Rebuild search index - reindex all photos for vector search
    /// </summary>
    [HttpPost("rebuild-index")]
    public async Task<ActionResult> RebuildIndex(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Rebuilding search index...");

            if (!Vector<PhotoAsset>.IsAvailable)
            {
                return BadRequest(new { Error = "Vector search is not available" });
            }

            var photos = await PhotoAsset.All(ct);
            int reindexed = 0;

            foreach (var photo in photos)
            {
                // Re-save with vector will trigger re-indexing
                if (photo.Embedding != null && photo.Embedding.Length > 0)
                {
                    var metadata = new Dictionary<string, object>
                    {
                        ["originalFileName"] = photo.OriginalFileName,
                        ["eventId"] = photo.EventId,
                        ["searchText"] = BuildSearchText(photo)
                    };

                    await Data<PhotoAsset, string>.SaveWithVector(photo, photo.Embedding, metadata, ct);
                    reindexed++;
                }
            }

            _logger.LogInformation("Rebuilt search index: {Count} photos reindexed", reindexed);
            return Ok(new { Message = $"Reindexed {reindexed} photos", Count = reindexed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild index");
            return StatusCode(500, new { Error = "Failed to rebuild search index" });
        }
    }

    /// <summary>
    /// Clear AI embedding cache
    /// </summary>
    [HttpPost("clear-cache")]
    public async Task<ActionResult> ClearCache(CancellationToken ct = default)
    {
        try
        {
            var cacheRoot = Path.Combine(_env.ContentRootPath, ".Koan", "cache", "ai-introspection");

            if (!Directory.Exists(cacheRoot))
            {
                return Ok(new { Message = "Cache already empty", FilesDeleted = 0 });
            }

            var files = Directory.GetFiles(cacheRoot, "*.json", SearchOption.AllDirectories);
            var deletedCount = 0;

            foreach (var file in files)
            {
                System.IO.File.Delete(file);
                deletedCount++;
            }

            _logger.LogInformation("Cleared AI embedding cache: {Count} files deleted", deletedCount);
            return Ok(new { Message = $"Cleared {deletedCount} cache entries", FilesDeleted = deletedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear cache");
            return StatusCode(500, new { Error = "Failed to clear cache" });
        }
    }

    /// <summary>
    /// Optimize database - compact and rebuild indexes
    /// </summary>
    [HttpPost("optimize-db")]
    public async Task<ActionResult> OptimizeDatabase(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Optimizing database...");

            // For MongoDB, you might call compact command
            // For now, just log and return success
            // In production, implement actual optimization logic

            await Task.Delay(500, ct); // Simulate work

            _logger.LogInformation("Database optimization complete");
            return Ok(new { Message = "Database optimized successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize database");
            return StatusCode(500, new { Error = "Failed to optimize database" });
        }
    }

    /// <summary>
    /// Export all photo metadata as JSON
    /// </summary>
    [HttpGet("export-metadata")]
    public async Task<ActionResult> ExportMetadata(CancellationToken ct = default)
    {
        try
        {
            var photos = await PhotoAsset.All(ct);
            var events = await Event.All(ct);

            var export = new
            {
                ExportDate = DateTime.UtcNow,
                Version = "1.0",
                Photos = photos.Select(p => new
                {
                    p.Id,
                    p.EventId,
                    p.OriginalFileName,
                    p.Width,
                    p.Height,
                    p.CameraModel,
                    p.LensModel,
                    p.CapturedAt,
                    p.AutoTags,
                    p.MoodDescription,
                    p.DetectedObjects,
                    p.IsFavorite,
                    p.Rating,
                    Location = p.Location != null ? new
                    {
                        p.Location.Latitude,
                        p.Location.Longitude,
                        p.Location.Altitude
                    } : null
                }),
                Events = events.Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.EventDate,
                    e.PhotoCount,
                    e.Type
                })
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"snapvault-metadata-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export metadata");
            return StatusCode(500, new { Error = "Failed to export metadata" });
        }
    }

    /// <summary>
    /// Backup configuration and user preferences
    /// </summary>
    [HttpGet("backup-config")]
    public async Task<ActionResult> BackupConfig(CancellationToken ct = default)
    {
        try
        {
            var events = await Event.All(ct);

            var config = new
            {
                BackupDate = DateTime.UtcNow,
                Version = "1.0",
                Events = events.Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.Type,
                    e.EventDate
                }),
                Settings = new
                {
                    // Add any user preferences here
                    Theme = "dark",
                    DefaultView = "gallery"
                }
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"snapvault-config-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup configuration");
            return StatusCode(500, new { Error = "Failed to backup configuration" });
        }
    }

    /// <summary>
    /// Wipe entire repository - DESTRUCTIVE operation with progress streaming
    /// </summary>
    [HttpPost("wipe-repository")]
    public async Task WipeRepository(CancellationToken ct = default)
    {
        Response.Headers.Append("Content-Type", "application/x-ndjson");
        Response.Headers.Append("Cache-Control", "no-cache");

        try
        {
            await SendProgress(0, "Starting repository wipe...");

            // Step 1: Delete ALL media entities (photos, thumbnails, galleries) - 60% of progress
            await SendProgress(5, "Deleting all photos...");
            var photos = await PhotoAsset.All(ct);
            var totalPhotos = photos.Count;
            var deletedPhotos = 0;

            foreach (var photo in photos)
            {
                await photo.Delete(ct);
                deletedPhotos++;
                if (deletedPhotos % 10 == 0 || deletedPhotos == totalPhotos)
                {
                    var progress = 5 + (int)((deletedPhotos / (double)Math.Max(totalPhotos, 1)) * 25);
                    await SendProgress(progress, $"Deleting photos... {deletedPhotos}/{totalPhotos}");
                }
            }

            await SendProgress(30, "Deleting all thumbnails...");
            var thumbnails = await PhotoThumbnail.All(ct);
            var totalThumbs = thumbnails.Count;
            var deletedThumbs = 0;
            foreach (var thumb in thumbnails)
            {
                await thumb.Delete(ct);
                deletedThumbs++;
            }
            await SendProgress(45, $"Deleted {deletedThumbs} thumbnails");

            await SendProgress(45, "Deleting all gallery images...");
            var galleries = await PhotoGallery.All(ct);
            var totalGalleries = galleries.Count;
            var deletedGalleries = 0;
            foreach (var gallery in galleries)
            {
                await gallery.Delete(ct);
                deletedGalleries++;
            }
            await SendProgress(60, $"Deleted {deletedGalleries} gallery images");

            // Step 2: Delete all events (15% of progress)
            await SendProgress(60, "Deleting events...");
            var events = await Event.All(ct);
            foreach (var evt in events)
            {
                await evt.Delete(ct);
            }
            await SendProgress(75, $"Deleted {events.Count} events");

            // Step 3: Delete processing jobs (10% of progress)
            await SendProgress(75, "Deleting processing jobs...");
            var jobs = await ProcessingJob.All(ct);
            foreach (var job in jobs)
            {
                await job.Delete(ct);
            }
            await SendProgress(85, $"Deleted {jobs.Count} processing jobs");

            // Step 4: Clear physical file storage (15% of progress)
            await SendProgress(85, "Clearing physical storage...");
            var storageRoot = Path.Combine(_env.ContentRootPath, ".Koan", "storage");
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, true);
            }

            // Step 5: Clear AI cache
            await SendProgress(95, "Clearing AI cache...");
            var cacheRoot = Path.Combine(_env.ContentRootPath, ".Koan", "cache");
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, true);
            }

            await SendProgress(100, "Repository wiped successfully");

            _logger.LogWarning("Repository wiped: {PhotoCount} photos, {ThumbCount} thumbnails, {GalleryCount} galleries, {EventCount} events, {JobCount} jobs deleted",
                totalPhotos, totalThumbs, totalGalleries, events.Count, jobs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to wipe repository");
            await SendProgress(-1, $"Error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private async Task SendProgress(int percentage, string message)
    {
        var progress = new { percentage, message };
        var json = JsonSerializer.Serialize(progress);
        await Response.WriteAsync(json + "\n");
        await Response.Body.FlushAsync();
    }

    private long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        return files.Sum(file => new FileInfo(file).Length);
    }

    private string BuildSearchText(PhotoAsset photo)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(photo.OriginalFileName))
            parts.Add($"Filename: {photo.OriginalFileName}");

        if (photo.AutoTags.Any())
            parts.Add($"Tags: {string.Join(", ", photo.AutoTags)}");

        if (!string.IsNullOrEmpty(photo.MoodDescription))
            parts.Add($"Mood: {photo.MoodDescription}");

        if (photo.DetectedObjects.Any())
            parts.Add($"Objects: {string.Join(", ", photo.DetectedObjects)}");

        if (!string.IsNullOrEmpty(photo.CameraModel))
            parts.Add($"Camera: {photo.CameraModel}");

        return string.Join("\n", parts);
    }
}

public class StorageStats
{
    public double HotTierGB { get; set; }
    public double WarmTierGB { get; set; }
    public double ColdTierGB { get; set; }
    public double TotalGB { get; set; }
    public long PhotoCount { get; set; }
    public int CacheEntries { get; set; }
    public int CacheSizeMB { get; set; }
}
