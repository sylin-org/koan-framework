using AnimeRecommendations.Domain;
using AnimeRecommendations.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AnimeRecommendations.Controllers;

[ApiController]
[Route(AnimeRecommendationsConstants.Routes.Recommendations)]
public sealed class RecommendationsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RecommendationFeed>> Get(
        [FromQuery] string viewerId = "demo",
        [FromQuery] string? mood = null,
        [FromQuery] int take = AnimeRecommendationsConstants.Limits.DefaultRecommendations,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await AnimeDiscovery.Recommend(viewerId, mood, take, ct));
        }
        catch (KeyNotFoundException error)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Viewer not found",
                Detail = error.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Recommendation intent is missing",
                Detail = error.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (ArgumentException error)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Recommendation request is invalid",
                Detail = error.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
