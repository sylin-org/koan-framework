using Microsoft.AspNetCore.Mvc;
using S18.Prism.Models;

namespace S18.Prism.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly ILogger<SearchController> _logger;

    public SearchController(ILogger<SearchController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// POST /api/search
    /// Semantic search across notes in one or more spaces
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(new { Error = "query is required" });

            _logger.LogInformation(
                "Searching for '{Query}' in spaces [{Spaces}]",
                request.Query,
                request.SpaceIds is { Count: > 0 } ? string.Join(", ", request.SpaceIds) : "all");

            // Text-based search across notes (vector search will be wired when embeddings are populated)
            var notes = request.SpaceIds is { Count: > 0 }
                ? await Note.Query(n => request.SpaceIds.Contains(n.SpaceId), ct)
                : await Note.All(ct);

            var queryLower = request.Query.ToLowerInvariant();
            var results = notes
                .Where(n =>
                    (n.Title?.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    n.KeyConcepts.Any(c => c.Contains(request.Query, StringComparison.OrdinalIgnoreCase)) ||
                    (n.Summary?.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    n.Blocks.Any(b => b.Content.Contains(request.Query, StringComparison.OrdinalIgnoreCase)))
                .Take(request.MaxResults)
                .Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Summary,
                    n.SpaceId,
                    n.KeyConcepts,
                    n.Category,
                    n.CreatedAt
                })
                .ToList();

            return Ok(new
            {
                Query = request.Query,
                Count = results.Count,
                Results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query '{Query}'", request.Query);
            return StatusCode(500, new { Error = "Search failed" });
        }
    }
}

public class SearchRequest
{
    public string Query { get; set; } = "";
    public List<string>? SpaceIds { get; set; }
    public int MaxResults { get; set; } = 20;
}
