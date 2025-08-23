using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Anime)] // Utility controller to fetch anime metadata for UI joins
public class AnimeController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var doc = await AnimeDoc.Get(id, ct);
        if (doc is null) return NotFound();
        return Ok(doc);
    }

    [HttpGet("by-ids")]
    public async Task<IActionResult> GetByIds([FromQuery] string ids, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids)) return Ok(Array.Empty<AnimeDoc>());
        var list = new List<AnimeDoc>();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var a = await AnimeDoc.Get(id, ct);
            if (a != null) list.Add(a);
        }
        return Ok(list);
    }
}
