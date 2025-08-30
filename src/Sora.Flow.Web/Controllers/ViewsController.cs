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
        if (!string.IsNullOrWhiteSpace(q))
            return Ok(await ProjectionView<object>.Query(q!, view, ct));
        var p = Math.Max(1, page ?? 1);
        var s = Math.Clamp(size ?? 50, 1, 500);
        return Ok(await ProjectionView<object>.Page(p, s, ct));
    }
}
