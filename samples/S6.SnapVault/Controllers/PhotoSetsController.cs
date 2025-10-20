using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using Koan.Data.Core;

namespace S6.SnapVault.Controllers;

/// <summary>
/// PhotoSet Session API
/// Manages stateful photo browsing contexts with persistent sessions
/// Uses custom session-aware endpoints instead of standard CRUD
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
    /// Unified PhotoSet Query Endpoint
    /// Creates session on first request, reuses session on subsequent requests
    /// This is the primary endpoint for all photo set navigation
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
                        // Session expired/deleted - recreate from definition
                        _logger.LogWarning(
                            "[PhotoSets] Session {SessionId} not found, creating new session",
                            request.SessionId);

                        session = await _service.GetOrCreateSessionAsync(request.Definition, ct);
                    }
                    else
                    {
                        return NotFound(new { error = $"Session {request.SessionId} not found" });
                    }
                }
                else
                {
                    // Update analytics
                    session.LastAccessedAt = DateTimeOffset.UtcNow;
                    session.ViewCount++;
                    await session.Save(ct);
                }
            }
            else if (request.Definition != null)
            {
                // Get or create session from definition
                session = await _service.GetOrCreateSessionAsync(request.Definition, ct);
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

            // Load photo metadata using Koan Entity patterns
            var photos = await LoadPhotoMetadataAsync(photoIds, ct);

            return Ok(new PhotoSetQueryResponse
            {
                SessionId = session.Id,
                SessionName = session.Name,
                SessionDescription = session.Description,
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
    /// Get specific session by ID
    /// </summary>
    /// <param name="id">Session ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Session details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<PhotoSetSession>> GetSession(
        string id,
        CancellationToken ct = default)
    {
        try
        {
            var session = await PhotoSetSession.Get(id, ct);
            if (session == null)
            {
                return NotFound(new { error = $"Session {id} not found" });
            }

            return Ok(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoSets] Get session failed");
            return StatusCode(500, new { error = "Failed to get session", details = ex.Message });
        }
    }

    /// <summary>
    /// List all sessions
    /// Supports filtering by pinned status and pagination
    /// </summary>
    /// <param name="pinnedOnly">Only return pinned sessions</param>
    /// <param name="limit">Maximum number of sessions to return</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of sessions ordered by last access time</returns>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PhotoSetSession>>> ListSessions(
        [FromQuery] bool pinnedOnly = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        try
        {
            IEnumerable<PhotoSetSession> sessions;

            if (pinnedOnly)
            {
                sessions = await PhotoSetSession.Query(s => s.IsPinned, ct);
            }
            else
            {
                sessions = await PhotoSetSession.All(ct);
            }

            // Order by last accessed (most recent first)
            sessions = sessions
                .OrderByDescending(s => s.LastAccessedAt)
                .Take(limit);

            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoSets] List sessions failed");
            return StatusCode(500, new { error = "Failed to list sessions", details = ex.Message });
        }
    }

    /// <summary>
    /// Update session metadata (name, pinned, color, icon)
    /// Does NOT rebuild photo list - use /refresh for that
    /// </summary>
    /// <param name="id">Session ID</param>
    /// <param name="request">Update request with optional fields</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Updated session</returns>
    [HttpPatch("{id}")]
    public async Task<ActionResult<PhotoSetSession>> UpdateSession(
        string id,
        [FromBody] PhotoSetUpdateRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var session = await PhotoSetSession.Get(id, ct);
            if (session == null)
            {
                return NotFound(new { error = $"Session {id} not found" });
            }

            // Update only provided fields
            if (request.Name != null)
            {
                session.Name = request.Name;
            }

            if (request.Description != null)
            {
                session.Description = request.Description;
            }

            if (request.IsPinned.HasValue)
            {
                session.IsPinned = request.IsPinned.Value;
            }

            if (request.Color != null)
            {
                session.Color = request.Color;
            }

            if (request.Icon != null)
            {
                session.Icon = request.Icon;
            }

            await session.Save(ct);

            _logger.LogInformation("[PhotoSets] Updated session {SessionId}", id);

            return Ok(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoSets] Update session failed");
            return StatusCode(500, new { error = "Failed to update session", details = ex.Message });
        }
    }

    /// <summary>
    /// Refresh session with current photo set
    /// Rebuilds PhotoIds snapshot - useful after bulk edits or imports
    /// </summary>
    /// <param name="id">Session ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Refreshed session</returns>
    [HttpPost("{id}/refresh")]
    public async Task<ActionResult<PhotoSetSession>> RefreshSession(
        string id,
        CancellationToken ct = default)
    {
        try
        {
            var session = await _service.RefreshSessionAsync(id, ct);
            return Ok(session);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Session {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoSets] Refresh session failed");
            return StatusCode(500, new { error = "Failed to refresh session", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete session
    /// </summary>
    /// <param name="id">Session ID</param>
    /// <param name="ct">Cancellation token</param>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        try
        {
            var session = await PhotoSetSession.Get(id, ct);
            if (session == null)
            {
                return NotFound(new { error = $"Session {id} not found" });
            }

            await session.Remove(ct);

            _logger.LogInformation("[PhotoSets] Deleted session {SessionId}", id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PhotoSets] Delete session failed");
            return StatusCode(500, new { error = "Failed to delete session", details = ex.Message });
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
