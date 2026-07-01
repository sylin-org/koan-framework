using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;
using S6.SnapVault.Services;

namespace S6.SnapVault.Controllers;

/// <summary>
/// PhotoSet session API (UI endpoint #5) — the one endpoint behind the windowed grid + lightbox navigation. First
/// call sends a definition and gets a sessionId; later calls reuse it to window on demand. Reads inherit the ambient
/// access + tenancy axes (a studio operator is unconstrained within their tenant).
/// </summary>
[ApiController]
[Route("api/photosets")]
public sealed class PhotoSetsController : ControllerBase
{
    private readonly PhotoSetService _service;

    public PhotoSetsController(PhotoSetService service) => _service = service;

    [HttpPost("query")]
    public async Task<ActionResult<PhotoSetQueryResponse>> Query([FromBody] PhotoSetQueryRequest request, CancellationToken ct = default)
    {
        PhotoSetSession? session = null;

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            session = await PhotoSetSession.Get(request.SessionId, ct);
            if (session == null && request.Definition == null)
                return NotFound(new { error = $"Session {request.SessionId} not found" });
        }

        // No live session (new browse, or an expired/missing session with a definition to re-create from).
        session ??= request.Definition != null
            ? await _service.CreateSession(request.Definition, ct)
            : null;

        if (session == null)
            return BadRequest(new { error = "Must provide sessionId or definition" });

        var photoAssets = await _service.ExecuteQuery(session, request.StartIndex, request.Count, ct);
        var photos = photoAssets.Select(PhotoMetadata.From).ToList();

        return Ok(new PhotoSetQueryResponse
        {
            SessionId = session.Id,
            Photos = photos,
            TotalCount = session.TotalCount,
            StartIndex = request.StartIndex,
            HasMore = request.StartIndex + photos.Count < session.TotalCount,
        });
    }
}
