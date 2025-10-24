using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Pipelines.Route)]
public sealed class PipelinesController : EntityController<DocumentPipeline>
{
    private readonly IPipelineBootstrapService _bootstrapService;

    public PipelinesController(IPipelineBootstrapService bootstrapService)
    {
        _bootstrapService = bootstrapService;
    }

    /// <summary>
    /// Creates a pipeline from file-only payload with embedded configuration.
    /// POST /api/pipelines/create
    /// Content-Type: multipart/form-data
    /// Files: analysis-config.json (required), plus one or more documents
    /// </summary>
    [HttpPost("create")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CreatePipelineResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFromFiles(
        [FromForm] IFormFileCollection files,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _bootstrapService.CreateFromFilesAsync(files, ct);
            return Created($"/api/pipelines/{response.PipelineId}", response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
