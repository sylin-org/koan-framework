using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Model;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("views")] // /views/{view}/{referenceId}
public sealed class ViewsController : ControllerBase
{
    [HttpGet("{view}/{referenceId}")]
    public async Task<IActionResult> GetOne([FromRoute] string view, [FromRoute] string referenceId, CancellationToken ct)
    {
        // Query by ReferenceId within the per-view set
        var list = await ProjectionView<object>.Query($"ReferenceId == '{referenceId}'", view, ct);
        var doc = list.FirstOrDefault();
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpGet("{view}")]
    public async Task<IActionResult> GetPage([FromRoute] string view, [FromQuery] string? q, [FromQuery] int? page = 1, [FromQuery] int? size = 50, CancellationToken ct = default)
    {
        var p = Math.Max(1, page ?? 1);
        var s = Math.Clamp(size ?? 50, 1, 500);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Filter within the per-view set
            var results = await ProjectionView<object>.Query(q!, view, ct);
            // naive in-memory paging for filtered queries
            var total = results.Count;
            var skip = (p - 1) * s;
            var pageItems = results.Skip(skip).Take(s).ToList();
            return Ok(new { page = p, size = s, total, items = pageItems });
        }

        // Unfiltered: page within the per-view set if supported. Fall back to Query + slice
        try
        {
            var paged = await ProjectionView<object>.Page(p, s, ct);
            return Ok(paged);
        }
        catch
        {
            var results = await ProjectionView<object>.Query("true", view, ct);
            var total = results.Count;
            var skip = (p - 1) * s;
            var pageItems = results.Skip(skip).Take(s).ToList();
            return Ok(new { page = p, size = s, total, items = pageItems });
        }
    }
}
