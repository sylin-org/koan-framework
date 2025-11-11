using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Deliverables.Route)]
public sealed class DeliverablesController : ControllerBase
{
    private readonly ITemplateRenderer _renderer;

    public DeliverablesController(ITemplateRenderer renderer)
    {
        _renderer = renderer;
    }

    [HttpGet("latest")]
    public async Task<ActionResult<Deliverable>> GetLatest(string pipelineId, CancellationToken ct)
    {
        var latest = await GetLatestDeliverable(pipelineId, ct).ConfigureAwait(false);
        if (latest is null)
        {
            return NotFound();
        }

        return latest;
    }

    [HttpGet("markdown")]
    public async Task<IActionResult> GetMarkdown(string pipelineId, CancellationToken ct)
    {
        var deliverable = await GetLatestDeliverable(pipelineId, ct).ConfigureAwait(false);
        if (deliverable is null)
        {
            return NotFound();
        }

        var markdown = await _renderer.RenderMarkdownAsync(deliverable, ct).ConfigureAwait(false);
        return Content(markdown, "text/markdown");
    }

    [HttpGet("json")]
    public async Task<IActionResult> GetJson(string pipelineId, CancellationToken ct)
    {
        var deliverable = await GetLatestDeliverable(pipelineId, ct).ConfigureAwait(false);
        if (deliverable is null)
        {
            return NotFound();
        }

        var json = await _renderer.RenderJsonAsync(deliverable, ct).ConfigureAwait(false);
        return Content(json, "application/json");
    }

    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf(string pipelineId, CancellationToken ct)
    {
        var deliverable = await GetLatestDeliverable(pipelineId, ct).ConfigureAwait(false);
        if (deliverable is null)
        {
            return NotFound();
        }

        var pdf = await _renderer.RenderPdfAsync(deliverable, ct).ConfigureAwait(false);
        if (pdf.Length == 0)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "PDF renderer unavailable.");
        }

        var fileName = string.IsNullOrWhiteSpace(deliverable.Id)
            ? $"{pipelineId}-deliverable.pdf"
            : $"{deliverable.Id}.pdf";

        return File(pdf, "application/pdf", fileName);
    }

    private static async Task<Deliverable?> GetLatestDeliverable(string pipelineId, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct).ConfigureAwait(false);
        if (pipeline is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(pipeline.DeliverableId))
        {
            var deliverable = await Deliverable.Get(pipeline.DeliverableId, ct).ConfigureAwait(false);
            if (deliverable is not null)
            {
                return deliverable;
            }
        }

        var deliverables = await Deliverable.Query(d => d.PipelineId == pipelineId, ct).ConfigureAwait(false);
        return deliverables
            .Where(d => string.Equals(d.PipelineId, pipelineId, StringComparison.Ordinal))
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();
    }
}
