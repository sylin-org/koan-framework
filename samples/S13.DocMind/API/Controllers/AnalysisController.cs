using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using S13.DocMind.Contracts;
using S13.DocMind.Models;
using S13.DocMind.Services;
using Koan.Web.Controllers;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController : EntityController<ManualAnalysisSession>
{
    private readonly IManualAnalysisService _sessions;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(IManualAnalysisService sessions, ILogger<AnalysisController> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManualAnalysisStatsResponse>> GetStatsAsync(CancellationToken cancellationToken)
    {
        var stats = await _sessions.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(stats);
    }

    [HttpGet("recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ManualAnalysisSummaryResponse>>> GetRecentAsync(CancellationToken cancellationToken, [FromQuery] int limit = 5)
    {
        var summaries = await _sessions.GetRecentAsync(limit, cancellationToken).ConfigureAwait(false);
        return Ok(summaries);
    }

    [HttpPost("{id}/run")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManualAnalysisRunResponse>> RunAsync([FromRoute] string id, [FromBody] ManualAnalysisRunRequest? request, CancellationToken cancellationToken)
    {
        var result = await _sessions.RunSessionAsync(id, request, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        _logger.LogInformation("Manual analysis session {SessionId} run via API", id);
        return Ok(result);
    }
}
