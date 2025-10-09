using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Services;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InsightsController : ControllerBase
{
    private readonly IDocumentInsightsService _insightsService;

    public InsightsController(IDocumentInsightsService insightsService)
    {
        _insightsService = insightsService;
    }

    [HttpGet("overview")]
    public async Task<ActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var overview = await _insightsService.GetOverviewAsync(cancellationToken);
        return Ok(overview);
    }

    [HttpGet("profiles/{profileId}")]
    public async Task<ActionResult> GetProfileCollections(string profileId, CancellationToken cancellationToken)
    {
        var collections = await _insightsService.GetProfileCollectionsAsync(profileId, cancellationToken);
        return Ok(collections);
    }

    [HttpGet("profiles")]
    public Task<ActionResult> GetAllProfiles(CancellationToken cancellationToken)
        => GetProfileCollections("all", cancellationToken);

    [HttpGet("feeds")]
    public async Task<ActionResult> GetFeeds(CancellationToken cancellationToken)
    {
        var feed = await _insightsService.GetAggregationFeedAsync(cancellationToken);
        return Ok(feed);
    }
}
