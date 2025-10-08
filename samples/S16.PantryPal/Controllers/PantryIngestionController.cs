using Koan.Data.Core;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Infrastructure;
using S16.PantryPal.Models;
using S16.PantryPal.Services;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Controllers;

[ApiController]
[Route(PantryRoutes.IngestionBase)]
public class PantryIngestionController(
    IPantryVisionService visionService,
    IPantryInputParser inputParser,
    IPantryConfirmationService confirmationService,
    IPhotoStorage photoStorage,
    IOptions<IngestionOptions>? ingestionOptions = null)
    : ControllerBase
{
    private readonly IngestionOptions _opts = ingestionOptions?.Value ?? new();
    [HttpPost("upload")]
    public async Task<IActionResult> UploadPhoto(
        [FromForm] IFormFile photo,
        [FromForm] bool detectQuantities = true,
        [FromForm] bool detectExpirationDates = true,
        [FromForm] string? userId = null,
        CancellationToken ct = default)
    {
        if (photo == null)
            return BadRequest(new { error = "Photo is required" });
        if (photo.Length == 0)
            return BadRequest(new { error = "Photo is empty" });
        if (photo.Length > _opts.MaxUploadBytes)
            return BadRequest(new { error = $"Photo exceeds max size {_opts.MaxUploadBytes} bytes" });
        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        if (_opts.AllowedExtensions.Count > 0 && !_opts.AllowedExtensions.Contains(ext))
            return BadRequest(new { error = $"File type '{ext}' not allowed" });

        // Persist photo through storage abstraction
        await using var uploadStream = photo.OpenReadStream();
        var storageKey = await photoStorage.StoreAsync(uploadStream, photo.FileName, photo.ContentType, ct);

        var photoRecord = new PantryPhoto
        {
            OriginalFileName = photo.FileName,
            StoragePath = storageKey,
            UploadedBy = userId,
            ProcessingStatus = "processing"
        };
        await photoRecord.Save();

        using var imageStream = await photoStorage.OpenReadAsync(storageKey, ct);

        // Thumbnails intentionally omitted (noise for MVP). A future enhancement could plug an image pipeline here.
        var options = new VisionProcessingOptions
        {
            DetectQuantities = detectQuantities,
            DetectExpirationDates = detectExpirationDates,
            UserId = userId
        };

        var result = await visionService.ProcessPhotoAsync(photoRecord.Id, imageStream, options, ct);

        // Idempotency / duplicate avoidance (basic): remove detections that already became pantry items for same photo
        if (result.Detections is { Length: >0 })
        {
            var existingItems = await PantryItem.Query(p => p.SourcePhotoId == photoRecord.Id, ct);
            var existingNames = existingItems.Select(i => (i.Name ?? string.Empty).Trim().ToLowerInvariant()).ToHashSet();
            foreach (var d in result.Detections)
            {
                // Use top candidate as canonical name for duplicate detection
                var candidateName = d.Candidates.FirstOrDefault()?.Name;
                if (!string.IsNullOrWhiteSpace(candidateName) && existingNames.Contains(candidateName.Trim().ToLowerInvariant()))
                {
                    d.Status = "duplicate";
                }
            }
        }

    photoRecord.ProcessingStatus = result.Success ? "completed" : "failed";
        photoRecord.Detections = result.Detections;
        photoRecord.ProcessingTimeMs = result.ProcessingTimeMs;
        photoRecord.Metrics = result.Metrics;
        await photoRecord.Save();

        return Ok(new
        {
            photoId = photoRecord.Id,
            detections = result.Detections,
            metrics = result.Metrics,
            processingTimeMs = result.ProcessingTimeMs
        });
    }

    [HttpPost("confirm/{photoId}")]
    public async Task<IActionResult> ConfirmDetections(string photoId, [FromBody] S16.PantryPal.Contracts.ConfirmDetectionsRequest request, CancellationToken ct = default)
    {
        try
        {
            var confirmed = await confirmationService.ConfirmDetectionsAsync(photoId, request.Confirmations, visionService, inputParser, ct);

            // Shelf-life inference (post-confirm) for items missing ExpiresAt
            foreach (var item in confirmed)
            {
                if (item.ExpiresAt == null && _opts.DefaultShelfLifeDaysByCategory.TryGetValue((item.Category ?? string.Empty).ToLowerInvariant(), out var days) && days > 0)
                {
                    item.ExpiresAt = DateTime.UtcNow.AddDays(days);
                    await item.Save();
                }
            }
            return Ok(new
            {
                confirmed = confirmed.Count,
                items = confirmed.Select(i => new { i.Id, i.Name, i.Quantity, i.Unit, i.ExpiresAt, i.Category })
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
