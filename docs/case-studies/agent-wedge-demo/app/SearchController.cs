using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.AspNetCore.Mvc;

namespace Recs;

// Beat 6: semantic search is a query, not a subsystem. Embed the query text with the same model the
// entity uses, then ask the vector store for nearest neighbours — no pipeline to build.
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (!Vector<Anime>.IsAvailable)
            return Ok(new { available = false, hint = "vector store not ready yet" });

        var queryVector = await Koan.AI.Client.Embed(q, ct);
        var result = await Vector<Anime>.Search(vector: queryVector, topK: 5, ct: ct);

        var hits = new List<object>();
        foreach (var match in result.Matches)
        {
            var anime = await Anime.Get(match.Id);
            if (anime is not null) hits.Add(new { anime.Title, anime.Synopsis, match.Score });
        }
        return Ok(new { query = q, hits });
    }
}
