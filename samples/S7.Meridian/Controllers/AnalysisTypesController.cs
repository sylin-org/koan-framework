using System;
using Koan.Samples.Meridian.Contracts;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.AnalysisTypeCatalog.Route)]
public sealed class AnalysisTypesController : EntityController<AnalysisType>
{
    private readonly IAnalysisTypeAuthoringService _authoring;
    private readonly ILogger<AnalysisTypesController> _logger;

    public AnalysisTypesController(
        IAnalysisTypeAuthoringService authoring,
        ILogger<AnalysisTypesController> logger)
    {
        _authoring = authoring;
        _logger = logger;
    }

    [HttpPost(MeridianConstants.AnalysisTypeCatalog.AiSuggestSegment)]
    public async Task<ActionResult<AnalysisTypeAiSuggestResponse>> SuggestAsync(
        [FromBody] AnalysisTypeAiSuggestRequest request,
        CancellationToken ct)
    {
        try
        {
            var response = await _authoring.SuggestAsync(request, ct).ConfigureAwait(false);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid analysis type AI suggest request.");
            return BadRequest(new { error = ex.Message });
        }
    }
}
