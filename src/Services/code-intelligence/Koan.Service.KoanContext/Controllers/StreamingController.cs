using System.Text;
using System.Text.Json;
using Koan.Context.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// Server-Sent Events (SSE) streaming controller for real-time updates
/// </summary>
[ApiController]
[Route("api/stream")]
public class StreamingController : ControllerBase
{
    private readonly ILogger<StreamingController> _logger;

    public StreamingController(ILogger<StreamingController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stream job progress updates in real-time via Server-Sent Events
    /// </summary>
    /// <param name="jobId">Job ID to monitor</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>
    /// SSE endpoint that streams job status updates.
    ///
    /// Example usage:
    /// ```javascript
    /// const eventSource = new EventSource('/api/stream/jobs/{jobId}');
    /// eventSource.onmessage = (event) => {
    ///   const data = JSON.parse(event.data);
    ///   console.log('Progress:', data.progress);
    /// };
    /// ```
    ///
    /// Event types:
    /// - `progress` - Job progress update
    /// - `status` - Job status change
    /// - `complete` - Job completed
    /// - `error` - Job failed
    /// - `heartbeat` - Keep-alive ping
    /// </remarks>
    /// <returns>SSE stream</returns>
    [HttpGet("jobs/{jobId}")]
    [Produces("text/event-stream")]
    public async Task StreamJobProgress(string jobId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SSE stream for job {JobId}", jobId);

        // Set SSE headers
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering

        try
        {
            // Check if job exists
            var job = await Job.Get(jobId, cancellationToken);
            if (job == null)
            {
                await SendSseEvent("error", new { message = "Job not found", jobId }, cancellationToken);
                return;
            }

            // Send initial status
            await SendJobUpdate(job, cancellationToken);

            var lastStatus = job.Status;
            var lastProgress = job.Progress;

            // Poll for updates
            while (!cancellationToken.IsCancellationRequested)
            {
                // Reload job
                job = await Job.Get(jobId, cancellationToken);
                if (job == null)
                {
                    await SendSseEvent("error", new { message = "Job deleted", jobId }, cancellationToken);
                    break;
                }

                // Check if status or progress changed
                var statusChanged = job.Status != lastStatus;
                var progressChanged = Math.Abs((double)(job.Progress - lastProgress)) > 0.01; // 1% threshold

                if (statusChanged || progressChanged)
                {
                    await SendJobUpdate(job, cancellationToken);
                    lastStatus = job.Status;
                    lastProgress = job.Progress;
                }

                // Check if job is terminal
                if (job.Status == JobStatus.Completed || job.Status == JobStatus.Failed || job.Status == JobStatus.Cancelled)
                {
                    await SendSseEvent("complete", new { jobId, status = job.Status.ToString() }, cancellationToken);
                    break;
                }

                // Send heartbeat every 15 seconds
                await SendSseEvent("heartbeat", new { timestamp = DateTime.UtcNow }, cancellationToken);

                // Wait before next poll (1 second)
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE stream cancelled for job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for job {JobId}", jobId);
            await SendSseEvent("error", new { message = "Stream error", details = ex.Message }, cancellationToken);
        }
    }

    /// <summary>
    /// Stream all active jobs progress
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SSE stream of all active jobs</returns>
    [HttpGet("jobs")]
    [Produces("text/event-stream")]
    public async Task StreamAllJobs(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SSE stream for all active jobs");

        // Set SSE headers
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            var lastSnapshot = new Dictionary<string, (JobStatus Status, decimal Progress)>();

            while (!cancellationToken.IsCancellationRequested)
            {
                // Query active jobs
                var activeJobs = await Job.Query(j =>
                    j.Status == JobStatus.Pending ||
                    j.Status == JobStatus.Planning ||
                    j.Status == JobStatus.Indexing);

                var jobsList = activeJobs.ToList();

                // Send updates for changed jobs
                foreach (var job in jobsList)
                {
                    var hasChanged = !lastSnapshot.TryGetValue(job.Id, out var last) ||
                                   last.Status != job.Status ||
                                   Math.Abs((double)(last.Progress - job.Progress)) > 0.01;

                    if (hasChanged)
                    {
                        await SendJobUpdate(job, cancellationToken);
                        lastSnapshot[job.Id] = (job.Status, job.Progress);
                    }
                }

                // Remove completed jobs from snapshot
                var completedJobIds = lastSnapshot
                    .Where(kvp => !jobsList.Any(j => j.Id == kvp.Key))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var completedId in completedJobIds)
                {
                    lastSnapshot.Remove(completedId);
                    await SendSseEvent("job-removed", new { jobId = completedId }, cancellationToken);
                }

                // Send heartbeat
                await SendSseEvent("heartbeat", new
                {
                    timestamp = DateTime.UtcNow,
                    activeJobs = jobsList.Count
                }, cancellationToken);

                // Wait before next poll
                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE stream for all jobs cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for all jobs");
            await SendSseEvent("error", new { message = "Stream error" }, cancellationToken);
        }
    }

    private async Task SendJobUpdate(Job job, CancellationToken cancellationToken)
    {
        var data = new
        {
            jobId = job.Id,
            projectId = job.ProjectId,
            status = job.Status.ToString(),
            progress = job.Progress,
            totalFiles = job.TotalFiles,
            processedFiles = job.ProcessedFiles,
            chunksCreated = job.ChunksCreated,
            vectorsSaved = job.VectorsSaved,
            startedAt = job.StartedAt,
            completedAt = job.CompletedAt,
            estimatedCompletion = job.EstimatedCompletion,
            elapsed = job.Elapsed,
            currentOperation = job.CurrentOperation,
            errorMessage = job.ErrorMessage
        };

        await SendSseEvent("job-update", data, cancellationToken);
    }

    private async Task SendSseEvent(string eventType, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var message = new StringBuilder();
        message.AppendLine($"event: {eventType}");
        message.AppendLine($"data: {json}");
        message.AppendLine(); // Empty line to complete the event

        var bytes = Encoding.UTF8.GetBytes(message.ToString());
        await Response.Body.WriteAsync(bytes, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
