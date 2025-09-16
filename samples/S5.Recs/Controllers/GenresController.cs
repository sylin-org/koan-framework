using Microsoft.AspNetCore.Mvc;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Controllers;

[ApiController]
[Route(Constants.Routes.Genres)]
public class GenresController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? sort = "popularity", [FromQuery] int? top = null, CancellationToken ct = default)
    {
        var list = await GenreStatDoc.All(ct);
        IEnumerable<GenreStatDoc> q = list;
        if (string.Equals(sort, "alpha", StringComparison.OrdinalIgnoreCase) || string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
            q = q.OrderBy(t => t.Genre);
        else
            q = q.OrderByDescending(t => t.MediaCount).ThenBy(t => t.Genre);
        if (top.HasValue && top.Value > 0) q = q.Take(top.Value);
        return Ok(q.Select(t => new { genre = t.Genre, count = t.MediaCount }));
    }
}
