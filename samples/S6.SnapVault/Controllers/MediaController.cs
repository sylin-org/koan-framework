using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using S6.SnapVault.Models;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Media serving controller - handles streaming of images at different resolutions
/// Separation of Concerns: Media delivery separate from metadata management
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class MediaController : ControllerBase
{
    private readonly ILogger<MediaController> _logger;

    public MediaController(ILogger<MediaController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Serve thumbnail image (150x150 square, hot-cdn tier)
    /// </summary>
    [HttpGet("photos/{id}/thumbnail")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetThumbnail(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        // Get thumbnail entity
        var thumbnail = await PhotoThumbnail.Get(photo.ThumbnailMediaId ?? "", ct);
        if (thumbnail == null)
        {
            _logger.LogWarning("Thumbnail not found for photo {PhotoId}", id);
            return NotFound();
        }

        // Stream directly from storage
        var stream = await thumbnail.OpenRead(ct);
        return File(stream, thumbnail.ContentType ?? "image/jpeg");
    }

    /// <summary>
    /// Serve masonry thumbnail image (300px max, aspect-ratio preserved, hot-cdn tier)
    /// </summary>
    [HttpGet("masonry-thumbnails/{id}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetMasonryThumbnail(string id, CancellationToken ct = default)
    {
        // Get masonry thumbnail entity directly
        var masonryThumbnail = await PhotoMasonryThumbnail.Get(id, ct);
        if (masonryThumbnail == null)
        {
            _logger.LogWarning("Masonry thumbnail not found for id {Id}", id);
            return NotFound();
        }

        // Stream directly from storage
        var stream = await masonryThumbnail.OpenRead(ct);
        return File(stream, masonryThumbnail.ContentType ?? "image/jpeg");
    }

    /// <summary>
    /// Serve gallery-size image (1200px max, warm tier)
    /// </summary>
    [HttpGet("photos/{id}/gallery")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetGallery(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        // Get gallery entity
        var gallery = await PhotoGallery.Get(photo.GalleryMediaId ?? "", ct);
        if (gallery == null)
        {
            _logger.LogWarning("Gallery image not found for photo {PhotoId}", id);
            return NotFound();
        }

        // Stream directly from storage
        var stream = await gallery.OpenRead(ct);
        return File(stream, gallery.ContentType ?? "image/jpeg");
    }

    /// <summary>
    /// Serve full-resolution original image (cold tier)
    /// </summary>
    [HttpGet("photos/{id}/original")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetOriginal(string id, CancellationToken ct = default)
    {
        var photo = await PhotoAsset.Get(id, ct);
        if (photo == null)
        {
            return NotFound();
        }

        // Increment view count (optimistic, fire-and-forget)
        _ = Task.Run(async () =>
        {
            photo.ViewCount++;
            await photo.Save(CancellationToken.None);
        });

        // Stream directly from storage
        var stream = await photo.OpenRead(ct);
        return File(stream, photo.ContentType ?? "image/jpeg", photo.OriginalFileName);
    }
}
