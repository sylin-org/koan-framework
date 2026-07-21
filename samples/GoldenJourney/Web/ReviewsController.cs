using Koan.GoldenJourney.Domain;
using Koan.GoldenJourney.Infrastructure;
using Koan.Data.Core;
using Koan.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace Koan.GoldenJourney.Web;

[ApiController]
[Route(GoldenJourneyConstants.Routes.Reviews)]
public sealed class ReviewsController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenReviewRequest input, CancellationToken ct)
    {
        try
        {
            var request = ReviewRequest.Open(input.Title, input.Impact, input.Urgent);
            await request.Save(ct);
            return CreatedAtAction(nameof(Get), new { id = request.Id }, request);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = GoldenJourneyConstants.Outcomes.InvalidTitle, error = exception.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var request = await ReviewRequest.Get(id, ct);
        return request is null ? NotFound() : Ok(request);
    }

    [HttpPost("{id}/assess")]
    public async Task<IActionResult> Assess(string id, CancellationToken ct)
    {
        var request = await ReviewRequest.Get(id, ct);
        if (request is null) return NotFound();

        var job = await request.Job.Submit(ct: ct);
        return Accepted(new { requestId = request.Id, jobId = job.JobId, status = JobStatus.Queued.ToString() });
    }

    [HttpGet("{id}/assessment")]
    public async Task<IActionResult> Assessment(string id, CancellationToken ct)
    {
        var records = await ReviewRequest.Jobs.Query(new JobQuery(WorkId: id), ct);
        var latest = records.OrderByDescending(record => record.FirstSubmittedAt).FirstOrDefault();
        if (latest is null) return NotFound();

        return Ok(new
        {
            requestId = id,
            jobId = latest.Id,
            status = latest.Status.ToString(),
            progress = latest.ProgressFraction,
            message = latest.ProgressMessage,
            error = latest.LastError
        });
    }
}
