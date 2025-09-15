using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Library)]
public class MediaLibraryController : ControllerBase
{
    // New: claim-based update (no userId in route)
    [Authorize]
    [HttpPut("by-me/{mediaId}")]
    public async Task<IActionResult> UpsertForMe(string mediaId, [FromBody] UpdateLibraryRequest body, CancellationToken ct)
    {
        var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return await Upsert(userId, mediaId, body, ct);
    }

    [HttpPut("{userId}/{mediaId}")]
    public async Task<IActionResult> Upsert(string userId, string mediaId, [FromBody] UpdateLibraryRequest body, CancellationToken ct)
    {
        var id = LibraryEntry.MakeId(userId, mediaId);
        var existing = await LibraryEntry.Get(id, ct);
        var entry = existing ?? new LibraryEntry
        {
            Id = id,
            UserId = userId,
            MediaId = mediaId,
            AddedAt = DateTimeOffset.UtcNow
        };

        // Apply updates
        if (body.Favorite is bool f) entry.Favorite = f;
        if (body.Status is MediaStatus s) entry.Status = s;
        if (body.Rating is int r) entry.Rating = Math.Clamp(r, 1, 10); // 1-10 scale
        if (body.Progress is int p) entry.Progress = Math.Max(0, p);
        if (body.Notes is string n) entry.Notes = n;

        // Legacy compatibility for old boolean fields
        if (body.Watched is bool w)
        {
            entry.Status = w ? MediaStatus.Completed : MediaStatus.PlanToConsume;
        }
        if (body.Dropped is bool d)
        {
            entry.Status = d ? MediaStatus.Dropped : MediaStatus.PlanToConsume;
        }

        // Reset entry if all flags are false and no rating
        if (body.Favorite == false && body.Status == MediaStatus.PlanToConsume && body.Rating is null)
        {
            entry.Favorite = false;
            entry.Status = MediaStatus.PlanToConsume;
            entry.Rating = null;
            entry.Progress = null;
            entry.Notes = null;
        }

        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await LibraryEntry.UpsertMany(new[] { entry }, ct);
        return Ok(entry);
    }

    [HttpDelete("{userId}/{mediaId}")]
    public async Task<IActionResult> Delete(string userId, string mediaId, CancellationToken ct)
    {
        var id = LibraryEntry.MakeId(userId, mediaId);
        await LibraryEntry.Remove(id, ct);
        return NoContent();
    }

    public sealed record ListQuery(
        string? Status = null,
        string? Sort = null,
        int Page = 1,
        int PageSize = 20,
        string? MediaType = null);

    // New: claim-based list (no userId in route)
    [Authorize]
    [HttpGet("by-me")]
    public async Task<IActionResult> ListForMe([FromQuery] ListQuery query, CancellationToken ct)
    {
        var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return await ListInternal(userId, query, ct);
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> List(string userId, [FromQuery] ListQuery query, CancellationToken ct)
    {
        return await ListInternal(userId, query, ct);
    }

    private static async Task<IActionResult> ListInternal(string userId, ListQuery query, CancellationToken ct)
    {
        var allEntries = (await LibraryEntry.All(ct)).Where(x => x.UserId == userId).ToList();

        // Filter by status
        var filtered = query.Status?.ToLowerInvariant() switch
        {
            "favorite" => allEntries.Where(x => x.Favorite),
            "consuming" => allEntries.Where(x => x.Status == MediaStatus.Consuming),
            "completed" => allEntries.Where(x => x.Status == MediaStatus.Completed),
            "dropped" => allEntries.Where(x => x.Status == MediaStatus.Dropped),
            "onhold" => allEntries.Where(x => x.Status == MediaStatus.OnHold),
            "plantoConsume" => allEntries.Where(x => x.Status == MediaStatus.PlanToConsume),
            // Legacy compatibility
            "watched" => allEntries.Where(x => x.Status == MediaStatus.Completed),
            _ => allEntries.AsEnumerable()
        };

        // Filter by media type if specified
        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            var mediaTypes = await MediaType.All(ct);
            var targetType = mediaTypes.FirstOrDefault(mt => mt.Name.Equals(query.MediaType, StringComparison.OrdinalIgnoreCase));
            if (targetType != null)
            {
                var mediaIds = filtered.Select(e => e.MediaId).ToHashSet();
                var mediaItems = (await Media.All(ct)).Where(m => mediaIds.Contains(m.Id!) && m.MediaTypeId == targetType.Id).ToList();
                var filteredMediaIds = mediaItems.Select(m => m.Id!).ToHashSet();
                filtered = filtered.Where(e => filteredMediaIds.Contains(e.MediaId));
            }
        }

        // Apply sorting
        IEnumerable<LibraryEntry> ordered = query.Sort?.ToLowerInvariant() switch
        {
            "addedat" => filtered.OrderByDescending(x => x.AddedAt),
            "rating" => filtered.OrderByDescending(x => x.Rating ?? 0),
            "progress" => filtered.OrderByDescending(x => x.Progress ?? 0),
            _ => filtered.OrderByDescending(x => x.UpdatedAt)
        };

        var skip = Math.Max(0, (query.Page - 1) * Math.Max(1, query.PageSize));
        var page = ordered.Skip(skip).Take(Math.Max(1, query.PageSize)).ToList();

        return new OkObjectResult(new { total = ordered.Count(), items = page });
    }
}