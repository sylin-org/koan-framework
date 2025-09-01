using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Model;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("lineage")] // /lineage/{referenceId}
public sealed class LineageController : ControllerBase
{
    [HttpGet("{referenceId}")]
    public async Task<IActionResult> Get([FromRoute] string referenceId, CancellationToken ct)
    {
        var item = await ReferenceItem.Get(referenceId, ct);
        if (item is null) return NotFound();
        // Minimal lineage: reference item + keys pointing to it
    var keys = await KeyIndex.Query($"ReferenceUlid == '{referenceId}'", ct);
        return Ok(new { reference = item, keys });
    }
}
