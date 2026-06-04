using Koan.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using S14.AdapterBench.Hubs;
using S14.AdapterBench.Jobs;
using S14.AdapterBench.Models;
using S14.AdapterBench.Services;

namespace S14.AdapterBench.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BenchmarkController : ControllerBase
{
    private readonly IBenchmarkService _benchmarkService;
    private readonly IHubContext<BenchmarkHub> _hubContext;

    public BenchmarkController(
        IBenchmarkService benchmarkService,
        IHubContext<BenchmarkHub> hubContext)
    {
        _benchmarkService = benchmarkService;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Start a new benchmark run as a background job.
    /// Returns a job ID immediately for polling the status.
    /// This is the recommended approach for long-running benchmarks.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<BenchmarkJobResponse>> RunBenchmark(
        [FromBody] BenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        // Start benchmark as a background job (in-memory, ephemeral). The request payload rides on the
        // work-item's Context (JOBS-0005 entity-first model); Submit persists + enqueues it.
        var work = new BenchmarkJob { Context = request };
        await work.Job.Submit("", cancellationToken);
        var status = await work.Job.Status(cancellationToken);

        return Ok(new BenchmarkJobResponse
        {
            JobId = work.Id,
            Status = (status ?? JobStatus.Queued).ToString(),
            Message = "Benchmark job started. Use GET /api/benchmark/status/{jobId} to check progress."
        });
    }

    /// <summary>
    /// Get the status and result of a benchmark job.
    /// Poll this endpoint to track progress and get final results.
    /// </summary>
    [HttpGet("status/{jobId}")]
    public async Task<ActionResult<BenchmarkJobStatusResponse>> GetBenchmarkStatus(
        string jobId,
        CancellationToken cancellationToken)
    {
        // Load the work-item (carries Result) and its latest ledger entry (carries lifecycle/progress).
        var work = await BenchmarkJob.Get(jobId, cancellationToken);
        if (work == null)
        {
            return NotFound(new { Message = $"Benchmark job {jobId} not found" });
        }

        var records = await BenchmarkJob.Jobs.Query(new JobQuery(WorkId: jobId), cancellationToken);
        var rec = records.OrderByDescending(r => r.FirstSubmittedAt).FirstOrDefault();
        var startedAt = rec?.Transitions.FirstOrDefault(t => t.To == JobStatus.Running)?.At;
        var completedAt = rec is { IsTerminal: true } ? rec.LastSettledAt : null;

        var response = new BenchmarkJobStatusResponse
        {
            JobId = jobId,
            Status = (rec?.Status ?? JobStatus.Queued).ToString(),
            Progress = rec?.ProgressFraction ?? 0,
            ProgressMessage = rec?.ProgressMessage,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Duration = startedAt.HasValue && completedAt.HasValue ? completedAt - startedAt : null,
            Result = work.Result,
            Error = rec?.LastError
        };

        return Ok(response);
    }

    /// <summary>
    /// Cancel a running benchmark job.
    /// </summary>
    [HttpPost("cancel/{jobId}")]
    public async Task<ActionResult> CancelBenchmark(
        string jobId,
        CancellationToken cancellationToken)
    {
        await BenchmarkJob.Jobs.Cancel(jobId, cancellationToken);
        return Ok(new { Message = $"Benchmark job {jobId} cancellation requested" });
    }

    /// <summary>
    /// Legacy synchronous endpoint - runs benchmark and waits for completion.
    /// Use POST /api/benchmark/run for better experience with long-running benchmarks.
    /// </summary>
    [HttpPost("run-sync")]
    public async Task<ActionResult<BenchmarkResult>> RunBenchmarkSync(
        [FromBody] BenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<BenchmarkProgress>(async p =>
        {
            // Send overall progress
            await _hubContext.Clients.Group("BenchmarkProgress")
                .SendAsync("ProgressUpdate", p, cancellationToken);

            // Send detailed provider progress for parallel mode visualization
            if (p.ProviderProgress != null && p.ProviderProgress.Count > 0)
            {
                await _hubContext.Clients.Group("BenchmarkProgress")
                    .SendAsync("ProviderProgressUpdate", p, cancellationToken);
            }
        });

        var result = await _benchmarkService.RunBenchmark(request, progress, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get a list of available data providers for benchmarking.
    /// </summary>
    [HttpGet("providers")]
    public ActionResult<List<ProviderInfo>> GetProviders()
    {
        return Ok(new List<ProviderInfo>
        {
            new() { Name = "sqlite", DisplayName = "SQLite", IsContainerized = false, IsDefault = true },
            new() { Name = "postgres", DisplayName = "PostgreSQL", IsContainerized = true, IsDefault = true },
            new() { Name = "mongo", DisplayName = "MongoDB", IsContainerized = true, IsDefault = true },
            new() { Name = "redis", DisplayName = "Redis", IsContainerized = true, IsDefault = true }
        });
    }

    /// <summary>
    /// Get available entity tiers for benchmarking.
    /// </summary>
    [HttpGet("tiers")]
    public ActionResult<List<TierInfo>> GetTiers()
    {
        return Ok(new List<TierInfo>
        {
            new() { Name = "Minimal", Description = "ID + timestamp only", IsDefault = true },
            new() { Name = "Indexed", Description = "Business entity with indexed properties", IsDefault = true },
            new() { Name = "Complex", Description = "Complex document with nested objects", IsDefault = true }
        });
    }
}

public class ProviderInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsContainerized { get; set; }
    public bool IsDefault { get; set; }
}

public class TierInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsDefault { get; set; }
}
