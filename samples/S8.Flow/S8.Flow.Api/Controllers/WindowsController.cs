using Microsoft.AspNetCore.Mvc;
using S8.Flow.Shared;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/windows")] // Read windowed analytics views
public sealed class WindowsController : ControllerBase
{
    [HttpGet("5m/{referenceId}")]
    public async Task<IActionResult> Get5m(string referenceId, CancellationToken ct)
    {
        using (DataSetContext.With(FlowSets.ViewShort(Hosting.WindowReadingProjector.ViewName)))
        {
            var doc = await Data<SensorWindowReading, string>.GetAsync($"{Hosting.WindowReadingProjector.ViewName}::{referenceId}", ct);
            return doc is null ? NotFound() : Ok(doc);
        }
    }
}
