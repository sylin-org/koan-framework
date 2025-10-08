using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Models;
using S16.PantryPal.Services;

namespace S16.PantryPal.Controllers;

/// <summary>
/// Primary pantry management controller.
/// Handles photo uploads, vision processing, and natural language input.
/// </summary>
[ApiController]
[Route("api/pantry")]
public class PantryController(
    IPantryVisionService visionService,
    IPantryInputParser inputParser) : ControllerBase
{
    /// <summary>
    /// Upload and process pantry photo with AI vision detection.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadPhoto(
        [FromForm] IFormFile photo,
        [FromForm] bool detectQuantities = true,
        [FromForm] bool detectExpirationDates = true,
        [FromForm] string? userId = null,
        CancellationToken ct = default)
    {
        if (photo == null || photo.Length == 0)
            return BadRequest(new { error = "Photo is required" });

        // Create photo record
        var photoRecord = new PantryPhoto
        {
            OriginalFileName = photo.FileName,
            StoragePath = $"photos/{Guid.NewGuid()}{Path.GetExtension(photo.FileName)}",
            UploadedBy = userId,
            ProcessingStatus = "processing"
        };

        await Data<PantryPhoto>.Upsert(photoRecord);

        // Save photo to storage
        var photoPath = Path.Combine("photos", Path.GetFileName(photoRecord.StoragePath));
        Directory.CreateDirectory(Path.GetDirectoryName(photoPath)!);

        await using (var stream = new FileStream(photoPath, FileMode.Create))
        {
            await photo.CopyToAsync(stream, ct);
        }

        // Process with vision AI
        using var imageStream = System.IO.File.OpenRead(photoPath);
        var options = new VisionProcessingOptions
        {
            DetectQuantities = detectQuantities,
            DetectExpirationDates = detectExpirationDates,
            UserId = userId
        };

        var result = await visionService.ProcessPhotoAsync(
            photoRecord.Id,
            imageStream,
            options,
            ct);

        // Update photo record with detections
        photoRecord.ProcessingStatus = result.Success ? "completed" : "failed";
        photoRecord.Detections = result.Detections;
        photoRecord.ProcessingTimeMs = result.ProcessingTimeMs;
        photoRecord.Metrics = result.Metrics;

        await Data<PantryPhoto>.Upsert(photoRecord);

        return Ok(new
        {
            photoId = photoRecord.Id,
            detections = result.Detections,
            metrics = result.Metrics,
            processingTimeMs = result.ProcessingTimeMs
        });
    }

    /// <summary>
    /// Confirm detected items and add to pantry inventory.
    /// </summary>
    [HttpPost("confirm/{photoId}")]
    public async Task<IActionResult> ConfirmDetections(
        string photoId,
        [FromBody] ConfirmDetectionsRequest request,
        CancellationToken ct = default)
    {
        var photo = await PantryPhoto.Get(photoId);
        if (photo == null)
            return NotFound(new { error = "Photo not found" });

        var confirmedItems = new List<PantryItem>();

        foreach (var confirmation in request.Confirmations)
        {
            var detection = photo.Detections.FirstOrDefault(d => d.Id == confirmation.DetectionId);
            if (detection == null)
                continue;

            // Parse user input for quantity/expiration
            ParsedItemData? parsedData = null;
            if (!string.IsNullOrWhiteSpace(confirmation.UserInput))
            {
                parsedData = inputParser.ParseInput(confirmation.UserInput);
            }

            // Get selected candidate
            var candidate = detection.Candidates.FirstOrDefault(c =>
                c.Id == (confirmation.SelectedCandidateId ?? detection.SelectedCandidateId));

            if (candidate == null)
                candidate = detection.Candidates.FirstOrDefault();

            if (candidate == null)
                continue;

            // Create pantry item
            var item = new PantryItem
            {
                Name = candidate.Name,
                Category = candidate.Category ?? "uncategorized",
                Quantity = parsedData?.Quantity ?? detection.ParsedData?.Quantity ?? 1,
                Unit = parsedData?.Unit ?? detection.ParsedData?.Unit ?? candidate.DefaultUnit ?? "whole",
                ExpiresAt = parsedData?.ExpiresAt ?? detection.ParsedData?.ExpiresAt,
                AddedAt = DateTime.UtcNow,
                Status = "available",
                VisionMetadata = new VisionMetadata
                {
                    SourcePhotoId = photoId,
                    DetectionId = detection.Id,
                    Confidence = candidate.Confidence,
                    WasUserCorrected = !string.IsNullOrWhiteSpace(confirmation.UserInput)
                }
            };

            await Data<PantryItem>.Upsert(item);
            confirmedItems.Add(item);

            // Learn from user corrections
            if (!string.IsNullOrWhiteSpace(confirmation.UserInput))
            {
                await visionService.LearnFromCorrectionAsync(
                    candidate.Name,
                    item.Name,
                    confirmation.UserInput,
                    ct);
            }

            // Mark detection as confirmed
            detection.Status = "confirmed";
        }

        // Update photo record
        photo.ItemsConfirmed = confirmedItems.Count;
        await photo.Save();

        return Ok(new
        {
            confirmed = confirmedItems.Count,
            items = confirmedItems.Select(i => new
            {
                i.Id,
                i.Name,
                i.Quantity,
                i.Unit,
                i.ExpiresAt,
                i.Category
            })
        });
    }

    /// <summary>
    /// Search pantry items by name, category, or natural language.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? query = null,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? expiringOnly = null,
        CancellationToken ct = default)
    {
        var items = await PantryItem.All();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lowerQuery = query.ToLowerInvariant();
            items = items.Where(i => i.Name.ToLowerInvariant().Contains(lowerQuery));
        }

        if (!string.IsNullOrWhiteSpace(category))
            items = items.Where(i => i.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(status))
            items = items.Where(i => i.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        if (expiringOnly == true)
        {
            var soon = DateTime.UtcNow.AddDays(7);
            items = items.Where(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= soon);
        }

        return Ok(items.OrderBy(i => i.ExpiresAt).ThenBy(i => i.Name).ToList());
    }

    /// <summary>
    /// Get pantry statistics and insights.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var items = (await PantryItem.All()).ToList();

        var totalItems = items.Count;
        var expiringInWeek = items.Count(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= DateTime.UtcNow.AddDays(7));
        var expiringInMonth = items.Count(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= DateTime.UtcNow.AddDays(30));
        var expired = items.Count(i => i.ExpiresAt.HasValue && i.ExpiresAt.Value <= DateTime.UtcNow);

        var byCategory = items
            .GroupBy(i => i.Category)
            .Select(g => new { category = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count);

        return Ok(new
        {
            totalItems,
            expiringInWeek,
            expiringInMonth,
            expired,
            byCategory
        });
    }
}

public class ConfirmDetectionsRequest
{
    public DetectionConfirmation[] Confirmations { get; set; } = Array.Empty<DetectionConfirmation>();
}

public class DetectionConfirmation
{
    public string DetectionId { get; set; } = "";
    public string? SelectedCandidateId { get; set; }
    public string? UserInput { get; set; }
}
