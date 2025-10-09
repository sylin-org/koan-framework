using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using S13.DocMind.Contracts;
using S13.DocMind.Models;
using S13.DocMind.Services;
using Koan.Web.Controllers;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/document-types")]
public sealed class TemplatesController : EntityController<SemanticTypeProfile>
{
    private readonly ITemplateSuggestionService _templates;
    private readonly ILogger<TemplatesController> _logger;

    public TemplatesController(ITemplateSuggestionService templates, ILogger<TemplatesController> logger)
    {
        _templates = templates;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<SemanticTypeProfileResponse>> GenerateAsync([FromBody] TemplateGenerationRequest request, CancellationToken cancellationToken)
    {
        var profile = await _templates.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Generated template {TemplateId}", profile.Id);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile.ToResponse());
    }

    [HttpPost("{id}/prompt-test")]
    public async Task<ActionResult<TemplatePromptTestResult>> PromptTestAsync([FromRoute] string id, [FromBody] TemplatePromptTestRequest request, CancellationToken cancellationToken)
    {
        var profile = await SemanticTypeProfile.Get(id, cancellationToken).ConfigureAwait(false);
        if (profile is null) return NotFound();
        var result = await _templates.RunPromptTestAsync(profile, request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}

internal static class TemplateMappingExtensions
{
    public static SemanticTypeProfileResponse ToResponse(this SemanticTypeProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Description = profile.Description,
        Category = profile.Category,
        Metadata = profile.Metadata,
        SystemPrompt = profile.Prompt.SystemPrompt,
        UserTemplate = profile.Prompt.UserTemplate,
        Variables = profile.Prompt.Variables
    };
}
