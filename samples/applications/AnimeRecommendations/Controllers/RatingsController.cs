using AnimeRecommendations.Domain;
using AnimeRecommendations.Infrastructure;
using Koan.Data.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AnimeRecommendations.Controllers;

public sealed record RateAnimeRequest(int Rating);

[ApiController]
[Route(AnimeRecommendationsConstants.Routes.ViewerRatings)]
public sealed class RatingsController : ControllerBase
{
    [HttpPut("{animeId}")]
    public async Task<ActionResult<LibraryEntry>> Put(
        string viewerId,
        string animeId,
        [FromBody] RateAnimeRequest request,
        CancellationToken ct)
    {
        if (await Viewer.Get(viewerId, ct) is null)
            return NotFound(Problem("Viewer not found", $"Viewer '{viewerId}' does not exist."));
        if (await Anime.Get(animeId, ct) is null)
            return NotFound(Problem("Anime not found", $"Anime '{animeId}' does not exist."));

        try
        {
            var entry = LibraryEntry.Record(viewerId, animeId, request.Rating);
            await entry.Save(ct);
            return Ok(entry);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return BadRequest(Problem("Rating is invalid", error.Message, StatusCodes.Status400BadRequest));
        }
    }

    [HttpDelete("{animeId}")]
    public async Task<IActionResult> Delete(string viewerId, string animeId, CancellationToken ct)
    {
        var entry = await LibraryEntry.Get(LibraryEntry.Key(viewerId, animeId), ct);
        if (entry is null)
            return NotFound(Problem("Rating not found", "There is no rating to remove."));

        await entry.Remove(ct);
        return NoContent();
    }

    private static ProblemDetails Problem(string title, string detail, int status = StatusCodes.Status404NotFound) =>
        new()
        {
            Title = title,
            Detail = detail,
            Status = status
        };
}
