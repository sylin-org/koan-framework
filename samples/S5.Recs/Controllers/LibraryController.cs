using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

public sealed record UpdateLibraryRequest(bool? Favorite, bool? Watched, bool? Dropped, int? Rating);

[ApiController]
[Route(Constants.Routes.Library)]
public class LibraryController : ControllerBase
{
    // New: claim-based update (no userId in route)
    [Authorize]
    [HttpPut("by-me/{animeId}")]
    public async Task<IActionResult> UpsertForMe(string animeId, [FromBody] UpdateLibraryRequest body, CancellationToken ct)
    {
        var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return await Upsert(userId, animeId, body, ct);
    }

    [HttpPut("{userId}/{animeId}")]
    public async Task<IActionResult> Upsert(string userId, string animeId, [FromBody] UpdateLibraryRequest body, CancellationToken ct)
    {
        var id = LibraryEntryDoc.MakeId(userId, animeId);
        var existing = await LibraryEntryDoc.Get(id, ct);
        var e = existing ?? new LibraryEntryDoc
        {
            Id = id,
            UserId = userId,
            AnimeId = animeId,
            AddedAt = DateTimeOffset.UtcNow
        };

        // Apply updates (Watched xor Dropped)
        if (body.Watched is bool w) { e.Watched = w; if (w) e.Dropped = false; }
        if (body.Dropped is bool d) { e.Dropped = d; if (d) e.Watched = false; }
        if (body.Favorite is bool f) e.Favorite = f;
        if (body.Rating is int r) e.Rating = Math.Clamp(r, 0, 5);

        // If nothing selected and no rating, treat as reset
        if (body.Favorite == false && body.Watched == false && body.Dropped == false && body.Rating is null)
        {
            e.Favorite = false; e.Watched = false; e.Dropped = false; e.Rating = null;
        }

        e.UpdatedAt = DateTimeOffset.UtcNow;
        await LibraryEntryDoc.UpsertMany(new[] { e }, ct);
        return Ok(e);
    }

    [HttpDelete("{userId}/{animeId}")]
    public async Task<IActionResult> Delete(string userId, string animeId, CancellationToken ct)
    {
        var id = LibraryEntryDoc.MakeId(userId, animeId);
        await LibraryEntryDoc.Remove(id, ct);
        return NoContent();
    }

    public sealed record ListQuery(string? Status = null, string? Sort = null, int Page = 1, int PageSize = 20);

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
        var all = (await LibraryEntryDoc.All(ct)).Where(x => x.UserId == userId).ToList();
        var filtered = query.Status?.ToLowerInvariant() switch
        {
            "favorite" => all.Where(x => x.Favorite),
            "watched" => all.Where(x => x.Watched),
            "dropped" => all.Where(x => x.Dropped),
            _ => all.AsEnumerable()
        };

        IEnumerable<LibraryEntryDoc> ordered = query.Sort?.ToLowerInvariant() switch
        {
            "addedat" => filtered.OrderByDescending(x => x.AddedAt),
            _ => filtered.OrderByDescending(x => x.UpdatedAt)
        };

        var skip = Math.Max(0, (query.Page - 1) * Math.Max(1, query.PageSize));
        var page = ordered.Skip(skip).Take(Math.Max(1, query.PageSize)).ToList();
        return new OkObjectResult(new { total = ordered.Count(), items = page });
    }
}

