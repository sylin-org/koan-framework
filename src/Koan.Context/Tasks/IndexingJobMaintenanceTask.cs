using Koan.Context.Models;
using Koan.Context.Services;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Tasks;

/// <summary>
/// Runs on app startup to maintain IndexingJob table health
/// </summary>
/// <remarks>
/// Maintenance operations:
/// 1. Recover stuck jobs (mark as Cancelled)
/// 2. Auto-resume interrupted jobs (optional, configurable)
/// 3. Clean up old terminal jobs (older than 7 days)
/// 4. Keep only most recent N jobs per project for audit history
/// </remarks>
internal sealed class IndexingJobMaintenanceTask : IScheduledTask, IOnStartup, IHasTimeout
{
    private readonly ILogger<IndexingJobMaintenanceTask> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly bool _autoResumeEnabled;
    private readonly int _autoResumeDelaySeconds;
    private const int MaxJobsToRetainPerProject = 50; // Keep last 50 jobs per project
    private const int OldJobRetentionDays = 7; // Clean up jobs older than 7 days

    public IndexingJobMaintenanceTask(
        ILogger<IndexingJobMaintenanceTask> logger,
        IServiceScopeFactory serviceScopeFactory,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _autoResumeEnabled = configuration.GetValue("Koan:Context:AutoResumeIndexing", true);
        _autoResumeDelaySeconds = configuration.GetValue("Koan:Context:AutoResumeDelay", 0);
    }

    public string Id => "koan-context:indexing-job-maintenance";

    public TimeSpan Timeout => TimeSpan.FromMinutes(2);

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Indexing job maintenance starting...");

        try
        {
            // 1. Recover stuck jobs (marks them as Cancelled)
            var (stuckCount, stuckJobsToResume) = await RecoverStuckJobsAsync(ct);

            // 2. Auto-resume interrupted jobs (if enabled)
            var resumedCount = 0;
            if (_autoResumeEnabled && stuckJobsToResume.Count > 0)
            {
                resumedCount = await AutoResumeJobsAsync(stuckJobsToResume, ct);
            }

            // 3. Clean up old terminal jobs
            var cleanedCount = await CleanupOldJobsAsync(ct);

            // 4. Enforce per-project job limits
            var trimmedCount = await TrimExcessJobsPerProjectAsync(ct);

            _logger.LogInformation(
                "Indexing job maintenance complete: {StuckRecovered} stuck jobs recovered, " +
                "{Resumed} jobs auto-resumed, {OldCleaned} old jobs cleaned up, {Trimmed} excess jobs trimmed",
                stuckCount,
                resumedCount,
                cleanedCount,
                trimmedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexing job maintenance failed - will retry on next startup");
        }
    }

    /// <summary>
    /// Recovers jobs left in non-terminal states from previous sessions
    /// </summary>
    /// <returns>Tuple of (count of recovered jobs, list of jobs to potentially resume)</returns>
    private async Task<(int count, List<(string projectId, JobStatus previousStatus)> jobsToResume)> RecoverStuckJobsAsync(CancellationToken ct)
    {
        var stuckJobs = await IndexingJob.Query(
            j => j.Status == JobStatus.Pending ||
                 j.Status == JobStatus.Planning ||
                 j.Status == JobStatus.Indexing,
            ct);

        var stuckJobsList = stuckJobs.ToList();

        if (stuckJobsList.Count == 0)
        {
            _logger.LogDebug("No stuck jobs found");
            return (0, new List<(string, JobStatus)>());
        }

        _logger.LogWarning(
            "Found {Count} stuck jobs from previous session - marking as cancelled",
            stuckJobsList.Count);

        var jobsToResume = new List<(string projectId, JobStatus previousStatus)>();

        foreach (var job in stuckJobsList)
        {
            var previousStatus = job.Status;
            var previousOperation = job.CurrentOperation;

            // Track jobs that were actively indexing (good candidates for auto-resume)
            if (previousStatus == JobStatus.Indexing)
            {
                jobsToResume.Add((job.ProjectId, previousStatus));
            }

            job.Cancel();
            job.ErrorMessage = $"Job cancelled during recovery. " +
                $"Previous state: {previousStatus}, Operation: {previousOperation ?? "(none)"}";

            await job.Save(ct);

            _logger.LogInformation(
                "Recovered stuck job {JobId} for project {ProjectId} - was {Status}",
                job.Id,
                job.ProjectId,
                previousStatus);
        }

        return (stuckJobsList.Count, jobsToResume);
    }

    /// <summary>
    /// Auto-resumes interrupted indexing jobs (seeding pattern)
    /// </summary>
    /// <remarks>
    /// Only resumes jobs that were actively indexing (not Pending/Planning).
    /// Verifies project still exists before resuming.
    /// Fire-and-forget pattern - doesn't block startup.
    /// Supports configurable delay to prevent immediate resource usage.
    /// </remarks>
    private async Task<int> AutoResumeJobsAsync(
        List<(string projectId, JobStatus previousStatus)> jobsToResume,
        CancellationToken ct)
    {
        var resumedCount = 0;

        if (_autoResumeDelaySeconds > 0)
        {
            _logger.LogInformation(
                "Auto-resume enabled with {Delay}s delay: scheduling {Count} interrupted indexing jobs",
                _autoResumeDelaySeconds,
                jobsToResume.Count);

            // Schedule delayed resume - doesn't block startup
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(_autoResumeDelaySeconds));
                await ResumeJobsInternalAsync(jobsToResume);
            }, CancellationToken.None);

            return jobsToResume.Count; // Return count of scheduled jobs
        }
        else
        {
            _logger.LogInformation(
                "Auto-resume enabled (immediate): attempting to resume {Count} interrupted indexing jobs",
                jobsToResume.Count);
        }

        return await ResumeJobsInternalAsync(jobsToResume);
    }

    /// <summary>
    /// Internal helper to resume jobs
    /// </summary>
    private async Task<int> ResumeJobsInternalAsync(List<(string projectId, JobStatus previousStatus)> jobsToResume)
    {
        var resumedCount = 0;

        foreach (var (projectId, previousStatus) in jobsToResume)
        {
            try
            {
                // Verify project still exists
                var project = await Project.Get(projectId);
                if (project == null)
                {
                    _logger.LogWarning(
                        "Skipping auto-resume for project {ProjectId} - project not found",
                        projectId);
                    continue;
                }

                // Skip if project is not active
                if (!project.IsActive)
                {
                    _logger.LogInformation(
                        "Skipping auto-resume for project {ProjectId} ({Name}) - project is inactive",
                        projectId,
                        project.Name);
                    continue;
                }

                _logger.LogInformation(
                    "Auto-resuming indexing for project {ProjectId} ({Name}) - was {PreviousStatus}",
                    projectId,
                    project.Name,
                    previousStatus);

                // Fire-and-forget - don't block startup
                // Capture serviceScopeFactory for use in Task.Run
                var serviceScopeFactory = _serviceScopeFactory;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Create a new scope to resolve scoped services
                        using var scope = serviceScopeFactory.CreateScope();
                        var indexingService = scope.ServiceProvider.GetRequiredService<IIndexingService>();

                        await indexingService.IndexProjectAsync(
                            projectId,
                            progress: null,
                            cancellationToken: CancellationToken.None,
                            force: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Auto-resume failed for project {ProjectId}",
                            projectId);
                    }
                }, CancellationToken.None);

                resumedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to auto-resume project {ProjectId}",
                    projectId);
            }
        }

        if (resumedCount > 0)
        {
            _logger.LogInformation(
                "Auto-resumed {Count} indexing jobs - they will continue in the background",
                resumedCount);
        }

        return resumedCount;
    }

    /// <summary>
    /// Cleans up old terminal jobs to prevent unbounded growth
    /// </summary>
    private async Task<int> CleanupOldJobsAsync(CancellationToken ct)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-OldJobRetentionDays);

        var oldJobs = await IndexingJob.Query(
            j => (j.Status == JobStatus.Completed ||
                  j.Status == JobStatus.Cancelled ||
                  j.Status == JobStatus.Failed) &&
                 j.CompletedAt.HasValue &&
                 j.CompletedAt.Value < cutoffDate,
            ct);

        var oldJobsList = oldJobs.ToList();

        if (oldJobsList.Count == 0)
        {
            _logger.LogDebug("No old jobs to clean up (retention: {Days} days)", OldJobRetentionDays);
            return 0;
        }

        _logger.LogInformation(
            "Cleaning up {Count} old jobs (older than {Days} days)",
            oldJobsList.Count,
            OldJobRetentionDays);

        foreach (var job in oldJobsList)
        {
            await IndexingJob.Remove(job.Id, ct);
        }

        return oldJobsList.Count;
    }

    /// <summary>
    /// Limits the number of jobs retained per project to prevent unbounded growth
    /// </summary>
    /// <remarks>
    /// Keeps the most recent N jobs per project, deletes older ones.
    /// This provides audit history while preventing table bloat.
    /// </remarks>
    private async Task<int> TrimExcessJobsPerProjectAsync(CancellationToken ct)
    {
        // Get all jobs grouped by project
        var allJobs = await IndexingJob.All(ct);
        var jobsByProject = allJobs
            .GroupBy(j => j.ProjectId)
            .ToList();

        var trimmedCount = 0;

        foreach (var projectGroup in jobsByProject)
        {
            var projectId = projectGroup.Key;
            var jobsList = projectGroup
                .OrderByDescending(j => j.StartedAt)
                .ToList();

            // If project has more than max allowed jobs, delete the oldest ones
            if (jobsList.Count > MaxJobsToRetainPerProject)
            {
                var jobsToDelete = jobsList.Skip(MaxJobsToRetainPerProject).ToList();

                _logger.LogInformation(
                    "Project {ProjectId} has {TotalJobs} jobs, trimming {ToDelete} oldest jobs " +
                    "(retention limit: {Limit})",
                    projectId,
                    jobsList.Count,
                    jobsToDelete.Count,
                    MaxJobsToRetainPerProject);

                foreach (var job in jobsToDelete)
                {
                    await IndexingJob.Remove(job.Id, ct);
                    trimmedCount++;
                }
            }
        }

        if (trimmedCount > 0)
        {
            _logger.LogInformation(
                "Trimmed {Count} excess jobs across all projects (limit: {Limit} per project)",
                trimmedCount,
                MaxJobsToRetainPerProject);
        }
        else
        {
            _logger.LogDebug("No excess jobs to trim (limit: {Limit} per project)", MaxJobsToRetainPerProject);
        }

        return trimmedCount;
    }
}
