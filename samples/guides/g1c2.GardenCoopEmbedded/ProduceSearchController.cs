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
        if (string.IsNullOrWhiteSpace(q)) return BadRequest("query parameter 'q' is required");

        var queryVector = await Client.Embed(q, ct);            // local ONNX embedding, via the AI facade
        var result = await Vector<Produce>.Search(queryVector, topK: k);

        var hits = new List<Hit>(result.Matches.Count);
        foreach (var match in result.Matches)
        {
            var id = (string)(object)match.Id;
            var produce = await Produce.Get(id);
            if (produce is not null)
                hits.Add(new Hit(id, produce.Name, produce.Category, match.Score));
        }
        return Ok(hits);
    }
}
