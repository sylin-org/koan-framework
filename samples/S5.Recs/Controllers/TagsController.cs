using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Tags)]
public class TagsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? sort = "popularity", [FromQuery] int? top = null, CancellationToken ct = default)
    {
        var list = await TagStatDoc.All(ct);
        IEnumerable<TagStatDoc> q = list;
        if (string.Equals(sort, "alpha", StringComparison.OrdinalIgnoreCase) || string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
            q = q.OrderBy(t => t.Tag);
        else
            q = q.OrderByDescending(t => t.AnimeCount).ThenBy(t => t.Tag);
        if (top.HasValue && top.Value > 0) q = q.Take(top.Value);
        return Ok(q.Select(t => new { tag = t.Tag, count = t.AnimeCount }));
    }
}
