using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Infrastructure;
using S16.PantryPal.Models;
using S16.PantryPal.Services;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Controllers;

[ApiController]
[Route(PantryRoutes.IngestionBase)]
public class PantryIngestionController(IPantryVisionService visionService, IPantryInputParser inputParser, IPantryConfirmationService confirmationService)
    : ControllerBase
{
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

        var photoRecord = new PantryPhoto
        {
            OriginalFileName = photo.FileName,
            StoragePath = $"photos/{Guid.NewGuid()}{Path.GetExtension(photo.FileName)}",
            UploadedBy = userId,
            ProcessingStatus = "processing"
        };
        await photoRecord.Save();

        var photoPath = Path.Combine("photos", Path.GetFileName(photoRecord.StoragePath));
        Directory.CreateDirectory(Path.GetDirectoryName(photoPath)!);
        await using (var stream = new FileStream(photoPath, FileMode.Create))
        {
            await photo.CopyToAsync(stream, ct);
        }

        using var imageStream = System.IO.File.OpenRead(photoPath);
        var options = new VisionProcessingOptions
        {
            DetectQuantities = detectQuantities,
            DetectExpirationDates = detectExpirationDates,
            UserId = userId
        };

        var result = await visionService.ProcessPhotoAsync(photoRecord.Id, imageStream, options, ct);

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
