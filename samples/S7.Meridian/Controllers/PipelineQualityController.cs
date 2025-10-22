using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.PipelineQuality.Route)]
public sealed class PipelineQualityController : ControllerBase
{
    private readonly IPipelineQualityDashboard _dashboard;

    public PipelineQualityController(IPipelineQualityDashboard dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet]
    public async Task<ActionResult<PipelineQualitySnapshot?>> GetLatest(string pipelineId, CancellationToken ct)
    {
        var snapshot = await _dashboard.GetLatestAsync(pipelineId, ct);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(snapshot);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<PipelineQualitySnapshot>>> GetHistory(string pipelineId, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            take = 10;
        }

        var history = await _dashboard.GetHistoryAsync(pipelineId, take, ct);
        return Ok(history);
    }
}
