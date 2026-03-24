using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S18.Prism.Models;
using S18.Prism.Services;

namespace S18.Prism.Controllers;

[Route("api/[controller]")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 200, DefaultSort = "-id")]
public class NotesController : EntityController<Note>
{
    private readonly INoteIngestionService _ingestion;
    private readonly ILogger<NotesController> _logger;

    public NotesController(
        INoteIngestionService ingestion,
        ILogger<NotesController> logger)
    {
        _ingestion = ingestion;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/notes/upload?spaceId={spaceId}
    /// Upload a file for ingestion as a Note
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        [FromQuery] string spaceId,
        CancellationToken ct = default)
    {
        try
        {
            if (file is null)
                return BadRequest(new { Error = "No file provided" });

            if (string.IsNullOrWhiteSpace(spaceId))
                return BadRequest(new { Error = "spaceId is required" });

            await using var stream = file.OpenReadStream();
            var note = await _ingestion.IngestFile(
                stream, file.FileName, file.ContentType, spaceId, ct);

            return CreatedAtAction("GetById", new { id = note.Id }, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName}", file?.FileName);
            return StatusCode(500, new { Error = "Failed to upload file" });
        }
    }

    /// <summary>
    /// POST /api/notes/text
    /// Ingest raw text as a Note
    /// </summary>
    [HttpPost("text")]
    public async Task<IActionResult> IngestText(
        [FromBody] IngestTextRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SpaceId))
                return BadRequest(new { Error = "spaceId is required" });

            if (string.IsNullOrWhiteSpace(request.Text))
                return BadRequest(new { Error = "text is required" });

            var note = await _ingestion.IngestText(
                request.Text, request.Title, request.SpaceId, ct);

            return CreatedAtAction("GetById", new { id = note.Id }, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest text");
            return StatusCode(500, new { Error = "Failed to ingest text" });
        }
    }

    /// <summary>
    /// POST /api/notes/{id}/rate
    /// Rate a note (1-5 stars)
    /// </summary>
    [HttpPost("{id}/rate")]
    public async Task<IActionResult> Rate(
        string id,
        [FromBody] RateRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var note = await Note.Get(id, ct);
            if (note is null)
                return NotFound(new { Error = $"Note '{id}' not found" });

            if (request.Rating < 1 || request.Rating > 5)
                return BadRequest(new { Error = "Rating must be between 1 and 5" });

            note.UserRating = request.Rating;
            await note.Save(ct);

            return Ok(new { note.Id, note.UserRating });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rate note {NoteId}", id);
            return StatusCode(500, new { Error = "Failed to rate note" });
        }
    }

    /// <summary>
    /// POST /api/notes/url
    /// Ingest content from a URL as a Note
    /// </summary>
    [HttpPost("url")]
    public async Task<IActionResult> IngestUrl(
        [FromBody] IngestUrlRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SpaceId))
                return BadRequest(new { Error = "spaceId is required" });

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { Error = "url is required" });

            var note = await _ingestion.IngestUrl(request.Url, request.SpaceId, ct);

            return CreatedAtAction("GetById", new { id = note.Id }, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest URL {Url}", request.Url);
            return StatusCode(500, new { Error = "Failed to ingest URL" });
        }
    }
}

public class IngestTextRequest
{
    public string SpaceId { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Title { get; set; }
}

public class IngestUrlRequest
{
    public string SpaceId { get; set; } = "";
    public string Url { get; set; } = "";
}

public class RateRequest
{
    public int Rating { get; set; }
}
