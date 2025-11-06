using Koan.Context.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Hosted service that recovers stuck jobs on application startup
/// </summary>
/// <remarks>
/// Detects IndexingJob entities left in non-terminal states (Planning, Indexing)
/// after application crashes or shutdowns. Marks them as Cancelled with context.
/// </remarks>
public class JobRecoveryService : IHostedService
{
    private readonly ILogger<JobRecoveryService> _logger;

    public JobRecoveryService(ILogger<JobRecoveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job recovery service starting - checking for stuck jobs...");

        try
        {
            // Find all jobs in non-terminal states
            var stuckJobs = await IndexingJob.Query(
                j => j.Status == JobStatus.Planning ||
                     j.Status == JobStatus.Indexing ||
                     j.Status == JobStatus.Pending,
                cancellationToken);

            var stuckJobsList = stuckJobs.ToList();

            if (stuckJobsList.Count == 0)
            {
                _logger.LogInformation("No stuck jobs found - recovery complete");
                return;
            }

            _logger.LogWarning(
                "Found {Count} stuck jobs from previous session - marking as cancelled",
                stuckJobsList.Count);

            foreach (var job in stuckJobsList)
            {
                var previousStatus = job.Status;
                var previousOperation = job.CurrentOperation;

                job.Cancel();
                job.ErrorMessage = $"Job cancelled during recovery. " +
                    $"Previous state: {previousStatus}, Operation: {previousOperation ?? "(none)"}";

                await IndexingJob.UpsertAsync(job, cancellationToken);

                _logger.LogInformation(
                    "Recovered stuck job {JobId} for project {ProjectId} - was {Status}",
                    job.Id,
                    job.ProjectId,
                    previousStatus);
            }

            _logger.LogInformation("Job recovery complete - {Count} jobs recovered", stuckJobsList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job recovery failed - will retry on next startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup needed
        return Task.CompletedTask;
    }
}
