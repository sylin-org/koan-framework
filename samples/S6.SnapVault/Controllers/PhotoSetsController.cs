using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using Koan.Data.Core;

namespace S6.SnapVault.Controllers;

/// <summary>
/// PhotoSet Session API
/// Single endpoint for session-based photo navigation
/// Sessions are volatile - new browser = new session
/// </summary>
[Route("api/photosets")]
[ApiController]
public class PhotoSetsController : ControllerBase
{
    private readonly PhotoSetService _service;
    private readonly ILogger<PhotoSetsController> _logger;

    public PhotoSetsController(
        PhotoSetService service,
        ILogger<PhotoSetsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// PhotoSet Query Endpoint
    /// Creates new session on first request, reuses session on subsequent navigation
    /// </summary>
    /// <param name="request">Query request with sessionId or definition</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>PhotoSet response with sessionId and photo range</returns>
    [HttpPost("query")]
    public async Task<ActionResult<PhotoSetQueryResponse>> Query(
        [FromBody] PhotoSetQueryRequest request,
        CancellationToken ct = default)
    {
        try
        {
            PhotoSetSession session;

            if (!string.IsNullOrEmpty(request.SessionId))
            {
                // Reuse existing session
                session = await PhotoSetSession.Get(request.SessionId, ct);

                if (session == null)
                {
                    if (request.Definition != null)
                    {
                        // Session expired/deleted - create new one
                        _logger.LogWarning(
                            "[PhotoSets] Session {SessionId} not found, creating new",
                            request.SessionId);

                        session = await _service.CreateSessionAsync(request.Definition, ct);
                    }
                    else
                    {
                        return NotFound(new { error = $"Session {request.SessionId} not found" });
                    }
                }
            }
            else if (request.Definition != null)
            {
                // Create new session
                session = await _service.CreateSessionAsync(request.Definition, ct);
            }
            else
            {
                return BadRequest(new { error = "Must provide sessionId or definition" });
            }

            // Get range from snapshot
            var photoIds = session.PhotoIds
                .Skip(request.StartIndex)
                .Take(request.Count)
                .ToList();

            // Load photo metadata
            var photos = await LoadPhotoMetadataAsync(photoIds, ct);

            return Ok(new PhotoSetQueryResponse
            {
                SessionId = session.Id,
                Photos = photos,
                TotalCount = session.TotalCount,
                StartIndex = request.StartIndex,
                HasMore = request.StartIndex + photos.Count < session.TotalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoSets] Query failed");
            return StatusCode(500, new { error = "Failed to query photo set", details = ex.Message });
        }
    }

    /// <summary>
    /// Load photo metadata for given IDs
    /// Maintains order from PhotoIds snapshot
    /// </summary>
    private async Task<List<PhotoMetadata>> LoadPhotoMetadataAsync(
        List<string> photoIds,
        CancellationToken ct = default)
    {
        var photos = new List<PhotoMetadata>();

        // Load photos in batches for efficiency
        const int batchSize = 100;
        for (int i = 0; i < photoIds.Count; i += batchSize)
        {
            var batch = photoIds.Skip(i).Take(batchSize).ToList();
            var batchPhotos = await PhotoAsset.Query(
                p => batch.Contains(p.Id),
                ct);

            // Maintain snapshot order
            var orderedBatch = batch
                .Select(id => batchPhotos.FirstOrDefault(p => p.Id == id))
                .Where(p => p != null)
                .Select(p => new PhotoMetadata
                {
                    Id = p!.Id,
                    FileName = p.OriginalFileName,
                    CapturedAt = p.CapturedAt,
                    CreatedAt = p.CreatedAt.UtcDateTime,
                    ThumbnailUrl = $"/api/media/photos/{p.Id}/thumbnail",
                    Rating = p.Rating,
                    IsFavorite = p.IsFavorite,
                    Width = p.Width,
                    Height = p.Height
                })
                .ToList();

            photos.AddRange(orderedBatch);
        }

        return photos;
    }
}
