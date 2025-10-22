using System.Collections.Generic;
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
    public async Task<ActionResult<DocumentIngestionResponse>> Upload(string pipelineId, [FromForm] List<IFormFile>? files, CancellationToken ct)
    {
        var collected = new FormFileCollection();

        if (files is { Count: > 0 })
        {
            foreach (var candidate in files)
            {
                if (candidate is { Length: > 0 })
                {
                    collected.Add(candidate);
                }
            }
        }

        if (collected.Count == 0 && Request.HasFormContentType)
        {
            foreach (var formFile in Request.Form.Files)
            {
                if (formFile is { Length: > 0 })
                {
                    collected.Add(formFile);
                }
            }
        }

        if (collected.Count == 0)
        {
            return BadRequest("At least one file is required.");
        }

        var documents = await _ingestion.IngestAsync(pipelineId, collected, ct).ConfigureAwait(false);
        var documentIds = documents
            .Select(d => d.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        var job = await _jobs.ScheduleAsync(pipelineId, documentIds, ct).ConfigureAwait(false);

        var response = new DocumentIngestionResponse
        {
            DocumentId = documentIds.FirstOrDefault() ?? string.Empty,
            DocumentIds = documentIds,
            JobId = job.Id,
            Status = job.Status.ToString()
        };

        return Accepted(response);
    }
}

public sealed class DocumentIngestionResponse
{
    public string DocumentId { get; set; } = string.Empty;
    public List<string> DocumentIds { get; set; } = new();
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
