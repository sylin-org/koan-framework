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
[Route(MeridianConstants.SourceTypeCatalog.Route)]
public sealed class SourceTypesController : EntityController<SourceType>
{
    private readonly ISourceTypeAuthoringService _authoring;
    private readonly ILogger<SourceTypesController> _logger;

    public SourceTypesController(
        ISourceTypeAuthoringService authoring,
        ILogger<SourceTypesController> logger)
    {
        _authoring = authoring;
        _logger = logger;
    }

    [HttpPost(MeridianConstants.SourceTypeCatalog.AiSuggestSegment)]
    public async Task<ActionResult<SourceTypeAiSuggestResponse>> SuggestAsync(
        [FromBody] SourceTypeAiSuggestRequest request,
        CancellationToken ct)
    {
        try
        {
            var response = await _authoring.SuggestAsync(request, ct).ConfigureAwait(false);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid source type AI suggest request.");
            return BadRequest(new { error = ex.Message });
        }
    }
}
