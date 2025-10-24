using System;
using Koan.Data.Core;
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

            if (response.Warnings?.Count > 0)
            {
                foreach (var warning in response.Warnings)
                {
                    _logger.LogWarning("AI analysis suggest warning: {Warning}", warning);
                }

                Response.Headers[MeridianConstants.Headers.AiWarnings] = string.Join(" | ", response.Warnings);
            }

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid analysis type AI suggest request.");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("ai-create")]
    public async Task<ActionResult<AnalysisType>> CreateWithAiAsync(
        [FromBody] AnalysisTypeAiSuggestRequest request,
        CancellationToken ct)
    {
        try
        {
            var response = await _authoring.SuggestAsync(request, ct).ConfigureAwait(false);
            var draft = response.Draft;

            if (response.Warnings?.Count > 0)
            {
                foreach (var warning in response.Warnings)
                {
                    _logger.LogWarning("AI analysis create warning: {Warning}", warning);
                }

                Response.Headers[MeridianConstants.Headers.AiWarnings] = string.Join(" | ", response.Warnings);
            }

            // Convert draft to entity and save
            var entity = new AnalysisType
            {
                Name = draft.Name,
                Description = draft.Description,
                Tags = draft.Tags,
                Descriptors = draft.Descriptors,
                Instructions = draft.Instructions,
                OutputTemplate = draft.OutputTemplate,
                JsonSchema = draft.JsonSchema
            };

            if (string.IsNullOrWhiteSpace(entity.Name))
            {
                entity.Name = "Untitled Analysis";
            }
            if (string.IsNullOrWhiteSpace(entity.Description))
            {
                entity.Description = "AI generated analysis type (no description provided).";
            }

            var saved = await entity.Save(ct).ConfigureAwait(false);
            _logger.LogInformation("AI-created analysis type '{Name}' with ID {Id}", saved.Name, saved.Id);
            return Ok(saved);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid AI create request.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create analysis type with AI.");
            return StatusCode(500, new { error = "Failed to create analysis type" });
        }
    }
}
