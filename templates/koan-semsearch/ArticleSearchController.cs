using Koan.AI;
using Koan.Data.Vector;
using Microsoft.AspNetCore.Mvc;

namespace KoanSemSearchApp;

[ApiController]
[Route("api/articles/search")]
public sealed class ArticleSearchController : ControllerBase
{
    public sealed record Hit(string Id, string Title, double Score);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Hit>>> Search(
        [FromQuery] string q,
        [FromQuery] int k = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required.");

        var queryVector = await Client.Embed(q, ct);
        var matches = await Vector<Article>.Search(
            queryVector,
            search => search.Top(Math.Clamp(k, 1, 20)),
            ct);
        var articles = await Article.Get(matches.Items.Select(match => match.Id), ct);

        return Ok(matches.Items.Zip(articles)
            .Where(pair => pair.Second is not null)
            .Select(pair => new Hit(
                pair.First.Id,
                pair.Second!.Title,
                pair.First.Similarity)));
    }
}
