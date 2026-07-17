using Koan.AI;
using Koan.Data.Vector;
using Microsoft.AspNetCore.Mvc;

namespace GardenCoopEmbedded;

/// <summary>
/// Semantic search over the co-op's produce, entirely in-process: the query text is embedded by the local
/// ONNX model (<see cref="Client.Embed(string, System.Threading.CancellationToken)"/>) and matched against
/// the sqlite-vec store. No model server, no vector server, no container.
/// </summary>
[ApiController]
[Route("api/produce/search")]
public sealed class ProduceSearchController : ControllerBase
{
    public sealed record Hit(string Id, string Name, string Category, double Score);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Hit>>> Search([FromQuery] string q, [FromQuery] int k = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required.");

        var queryVector = await Client.Embed(q, ct);
        var result = await Vector<Produce>.Search(queryVector, topK: Math.Clamp(k, 1, 20), ct: ct);
        var listings = await Produce.Get(result.Matches.Select(match => match.Id), ct);

        var hits = result.Matches.Zip(listings)
            .Where(pair => pair.Second is not null)
            .Select(pair => new Hit(
                pair.First.Id,
                pair.Second!.Name,
                pair.Second.Category,
                pair.First.Score));
        return Ok(hits);
    }
}
