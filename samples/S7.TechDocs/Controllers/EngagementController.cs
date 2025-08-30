using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Infrastructure;
using S7.TechDocs.Models;

namespace S7.TechDocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EngagementController : ControllerBase
{
    // Bookmark endpoints

    [HttpGet("bookmarks")] 
    [Authorize(Policy = "Reader")]
    public async Task<IActionResult> GetBookmarks()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var all = await Bookmark.All();
        var mine = all.Where(b => b.UserId == userId);
        return Ok(mine);
    }

    [HttpGet("bookmarks/{documentId}")]
    [Authorize(Policy = "Reader")]
    public async Task<IActionResult> IsBookmarked(string documentId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var id = $"{userId}:{documentId}";
        var bm = await Bookmark.Get(id);
        return Ok(new { bookmarked = bm != null });
    }

    [HttpPost("bookmarks/{documentId}")]
    [Authorize(Policy = "Reader")]
    public async Task<IActionResult> AddBookmark(string documentId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var id = $"{userId}:{documentId}";
        var existing = await Bookmark.Get(id);
        if (existing != null) return Ok(existing);
        var bm = new Bookmark { Id = id, DocumentId = documentId, UserId = userId, CreatedAt = DateTime.UtcNow };
    await Bookmark.Batch().Add(bm).SaveAsync();
        return Ok(bm);
    }

    [HttpDelete("bookmarks/{documentId}")]
    [Authorize(Policy = "Reader")]
    public async Task<IActionResult> RemoveBookmark(string documentId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var id = $"{userId}:{documentId}";
        await Bookmark.Remove(id);
        return NoContent();
    }

    // Ratings endpoints

    public record RateRequest(int Rating);

    [HttpPost("ratings/{documentId}")]
    [Authorize(Policy = "Reader")]
    public async Task<IActionResult> RateDocument(string documentId, [FromBody] RateRequest request)
    {
        if (request.Rating < 1 || request.Rating > 5) return BadRequest("Rating must be 1-5");
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var id = $"{userId}:{documentId}";
        var existing = await UserRating.Get(id);
        if (existing == null)
        {
            var rating = new UserRating
            {
                Id = id,
                DocumentId = documentId,
                UserId = userId,
                Rating = request.Rating,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await UserRating.Batch().Add(rating).SaveAsync();
        }
        else
        {
            await UserRating.Batch().Update(id, r => { r.Rating = request.Rating; r.UpdatedAt = DateTime.UtcNow; }).SaveAsync();
        }

        // Recompute aggregate rating on Document
        var doc = await Document.Get(documentId);
        if (doc != null)
        {
            var allRatings = (await UserRating.All()).Where(r => r.DocumentId == documentId).ToList();
            var avg = allRatings.Count > 0 ? allRatings.Average(r => r.Rating) : 0.0;
            await Document.Batch().Update(documentId, d => { d.Rating = avg; d.RatingCount = allRatings.Count; }).SaveAsync();
        }

        return Ok(new { success = true });
    }

    // Issues endpoints

    public record IssueRequest(string Type, string Description);

    [HttpPost("issues/{documentId}")]
    [Authorize(Policy = "Reader")]
    public async Task<IActionResult> ReportIssue(string documentId, [FromBody] IssueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description)) return BadRequest("Description required");
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var issue = new IssueReport
        {
            Id = $"issue-{Guid.NewGuid():N}",
            DocumentId = documentId,
            UserId = userId,
            Type = request.Type,
            Description = request.Description,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };
    await IssueReport.Batch().Add(issue).SaveAsync();
        return Ok(issue);
    }

    [HttpGet("issues")]
    [Authorize(Policy = "Moderator")]
    public async Task<IActionResult> ListIssues([FromQuery] string? documentId = null)
    {
        var all = await IssueReport.All();
        var filtered = string.IsNullOrEmpty(documentId) ? all : all.Where(i => i.DocumentId == documentId);
        var ordered = filtered.OrderByDescending(i => i.CreatedAt).ToList();
        return Ok(ordered);
    }

    public record IssueStatusRequest(string Status);

    [HttpPatch("issues/{issueId}")]
    [Authorize(Policy = "Moderator")]
    public async Task<IActionResult> UpdateIssueStatus(string issueId, [FromBody] IssueStatusRequest request)
    {
        var existing = await IssueReport.Get(issueId);
        if (existing == null) return NotFound();
    await IssueReport.Batch().Update(issueId, i => i.Status = request.Status).SaveAsync();
        var updated = await IssueReport.Get(issueId) ?? existing;
        return Ok(updated);
    }
}
