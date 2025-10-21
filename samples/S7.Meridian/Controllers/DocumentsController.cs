using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Documents.Route)]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;
    private readonly IJobCoordinator _jobs;

    public DocumentsController(IDocumentIngestionService ingestion, IJobCoordinator jobs)
    {
        _ingestion = ingestion;
        _jobs = jobs;
    }

    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<DocumentIngestionResponse>> Upload(string pipelineId, IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File required.");
        }

        var document = await _ingestion.IngestAsync(pipelineId, file, ct);
        var job = await _jobs.ScheduleAsync(pipelineId, new[] { document.Id }, ct);

        var response = new DocumentIngestionResponse
        {
            DocumentId = document.Id,
            JobId = job.Id,
            Status = job.Status.ToString()
        };

        return Accepted(response);
    }
}

public sealed class DocumentIngestionResponse
{
    public string DocumentId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
