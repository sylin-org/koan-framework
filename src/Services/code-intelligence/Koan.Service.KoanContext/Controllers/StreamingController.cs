using System.Runtime.CompilerServices;
using Koan.Context.Models;
using Koan.Web.Sse;
using Koan.Web.Sse.Mvc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Koan.Context.Controllers;

/// <summary>
/// Server-Sent Events (SSE) streaming controller for real-time updates
/// </summary>
[ApiController]
[Route("api/stream")]
public class StreamingController : ControllerBase
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };
    private static readonly TimeSpan JobPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AllJobsPollInterval = TimeSpan.FromSeconds(2);
    private const double ProgressChangeThreshold = 0.01;

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
    public IActionResult StreamJobProgress(string jobId, CancellationToken cancellationToken)
        => SseActionResult.StreamEnvelopes(StreamJobProgressCore(jobId, cancellationToken));

    /// <summary>
    /// Stream all active jobs progress
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SSE stream of all active jobs</returns>
    [HttpGet("jobs")]
    [Produces("text/event-stream")]
    public IActionResult StreamAllJobs(CancellationToken cancellationToken)
        => SseActionResult.StreamEnvelopes(StreamAllJobsCore(cancellationToken));

    private async IAsyncEnumerable<SseEnvelope> StreamJobProgressCore(string jobId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SSE stream for job {JobId}", jobId);

        var (job, fetchError, cancelled) = await TryGetJob(jobId, cancellationToken);
        if (cancelled)
        {
            _logger.LogInformation("SSE stream cancelled for job {JobId}", jobId);
            yield break;
        }

        if (fetchError != null)
        {
            _logger.LogError(fetchError, "Error in SSE stream for job {JobId}", jobId);
            yield return CreateEnvelope("error", new { message = "Stream error", details = fetchError.Message });
            yield break;
        }

        if (job == null)
        {
            yield return CreateEnvelope("error", new { message = "Job not found", jobId });
            yield break;
        }

        yield return CreateJobUpdate(job);

        var lastStatus = job.Status;
        var lastProgress = job.Progress;

        while (!cancellationToken.IsCancellationRequested)
        {
            var (currentJob, error, jobCancelled) = await TryGetJob(jobId, cancellationToken);
            if (jobCancelled)
            {
                _logger.LogInformation("SSE stream cancelled for job {JobId}", jobId);
                yield break;
            }

            if (error != null)
            {
                _logger.LogError(error, "Error in SSE stream for job {JobId}", jobId);
                yield return CreateEnvelope("error", new { message = "Stream error", details = error.Message });
                yield break;
            }

            job = currentJob;
            if (job == null)
            {
                yield return CreateEnvelope("error", new { message = "Job deleted", jobId });
                yield break;
            }

            var statusChanged = job.Status != lastStatus;
            var progressChanged = Math.Abs((double)(job.Progress - lastProgress)) > ProgressChangeThreshold;

            if (statusChanged || progressChanged)
            {
                yield return CreateJobUpdate(job);
                lastStatus = job.Status;
                lastProgress = job.Progress;
            }

            if (IsTerminal(job.Status))
            {
                yield return CreateEnvelope("complete", new { jobId, status = job.Status.ToString() });
                yield break;
            }

            yield return CreateEnvelope("heartbeat", new { timestamp = DateTime.UtcNow });

            if (!await Wait(JobPollInterval, cancellationToken))
            {
                _logger.LogInformation("SSE stream cancelled for job {JobId}", jobId);
                yield break;
            }
        }
    }

    private async IAsyncEnumerable<SseEnvelope> StreamAllJobsCore([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SSE stream for all active jobs");

        var lastSnapshot = new Dictionary<string, (JobStatus Status, decimal Progress)>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var (jobs, error, cancelled) = await TryQueryActiveJobs(cancellationToken);
            if (cancelled)
            {
                _logger.LogInformation("SSE stream for all jobs cancelled");
                yield break;
            }

            if (error != null)
            {
                _logger.LogError(error, "Error in SSE stream for all jobs");
                yield return CreateEnvelope("error", new { message = "Stream error" });
                yield break;
            }

            foreach (var job in jobs)
            {
                var hasChanged = !lastSnapshot.TryGetValue(job.Id, out var last) ||
                                 last.Status != job.Status ||
                                 Math.Abs((double)(last.Progress - job.Progress)) > ProgressChangeThreshold;

                if (hasChanged)
                {
                    yield return CreateJobUpdate(job);
                    lastSnapshot[job.Id] = (job.Status, job.Progress);
                }
            }

            var completedJobIds = lastSnapshot
                .Where(kvp => !jobs.Any(j => j.Id == kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var completedId in completedJobIds)
            {
                lastSnapshot.Remove(completedId);
                yield return CreateEnvelope("job-removed", new { jobId = completedId });
            }

            yield return CreateEnvelope("heartbeat", new
            {
                timestamp = DateTime.UtcNow,
                activeJobs = jobs.Count
            });

            if (!await Wait(AllJobsPollInterval, cancellationToken))
            {
                _logger.LogInformation("SSE stream for all jobs cancelled");
                yield break;
            }
        }
    }

    private static async Task<(Job? Job, Exception? Error, bool Cancelled)> TryGetJob(string jobId, CancellationToken cancellationToken)
    {
        try
        {
            var job = await Job.Get(jobId, cancellationToken);
            return (job, null, false);
        }
        catch (OperationCanceledException)
        {
            return (null, null, true);
        }
        catch (Exception ex)
        {
            return (null, ex, false);
        }
    }

    private static async Task<(IReadOnlyList<Job> Jobs, Exception? Error, bool Cancelled)> TryQueryActiveJobs(CancellationToken cancellationToken)
    {
        try
        {
            var activeJobs = await Job.Query(j =>
                j.Status == JobStatus.Pending ||
                j.Status == JobStatus.Planning ||
                j.Status == JobStatus.Indexing);

            return (activeJobs.ToList(), null, false);
        }
        catch (OperationCanceledException)
        {
            return (Array.Empty<Job>(), null, true);
        }
        catch (Exception ex)
        {
            return (Array.Empty<Job>(), ex, false);
        }
    }

    private static async Task<bool> Wait(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(interval, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static bool IsTerminal(JobStatus status)
        => status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;

    private static SseEnvelope CreateJobUpdate(Job job)
        => CreateEnvelope("job-update", new
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
        });

    private static SseEnvelope CreateEnvelope(string eventType, object payload)
    {
        var json = JsonConvert.SerializeObject(payload, SerializerSettings);
        return new SseEnvelope(eventType, json);
    }
}
