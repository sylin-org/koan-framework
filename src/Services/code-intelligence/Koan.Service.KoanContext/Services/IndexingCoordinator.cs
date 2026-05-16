using System.Collections.Concurrent;
using Koan.Context.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Coordinates indexing operations to prevent concurrent indexing of the same project
/// </summary>
/// <remarks>
/// Provides per-project locking and cancellation support for indexing operations.
/// Ensures only one indexing job runs per project at a time.
/// </remarks>
public class IndexingCoordinator
{
    private readonly ConcurrentDictionary<string, ProjectIndexingState> _projectStates = new();
    private readonly ILogger<IndexingCoordinator> _logger;

    public IndexingCoordinator(ILogger<IndexingCoordinator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts to acquire an indexing lock for a project
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="jobId">Job ID for this indexing operation</param>
    /// <param name="force">If true, cancels existing job and acquires lock</param>
    /// <returns>
    /// Success tuple with (acquired, existingJobId, cancellationToken)
    /// - acquired: true if lock was acquired
    /// - existingJobId: ID of existing job if one was running
    /// - cancellationToken: Token that will be cancelled if this job is forcefully replaced
    /// </returns>
    public (bool acquired, string? existingJobId, CancellationToken cancellationToken) TryAcquireLock(
        string projectId,
        string jobId,
        bool force = false)
    {
        var newState = new ProjectIndexingState(jobId);

        while (true)
        {
            var state = _projectStates.GetOrAdd(projectId, _ => newState);

            // If we just added our state, we have the lock
            if (ReferenceEquals(state, newState))
            {
                _logger.LogDebug(
                    "Acquired indexing lock for project {ProjectId} (Job: {JobId})",
                    projectId,
                    jobId);
                return (true, null, newState.CancellationToken);
            }

            // Another job is running
            var existingJobId = state.JobId;

            if (!force)
            {
                _logger.LogWarning(
                    "Project {ProjectId} is already being indexed by job {ExistingJobId}. " +
                    "Use force=true to cancel and restart.",
                    projectId,
                    existingJobId);
                return (false, existingJobId, CancellationToken.None);
            }

            // Force mode: cancel existing job and try to replace
            _logger.LogWarning(
                "Force restart requested for project {ProjectId}. " +
                "Cancelling existing job {ExistingJobId} and starting new job {NewJobId}",
                projectId,
                existingJobId,
                jobId);

            state.Cancel();

            // Try to replace the state with ours
            if (_projectStates.TryUpdate(projectId, newState, state))
            {
                _logger.LogInformation(
                    "Successfully replaced job {OldJobId} with {NewJobId} for project {ProjectId}",
                    existingJobId,
                    jobId,
                    projectId);
                return (true, existingJobId, newState.CancellationToken);
            }

            // Another thread beat us to it, try again
            _logger.LogDebug("Race condition detected, retrying lock acquisition for project {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// Releases the indexing lock for a project
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <param name="jobId">Job ID that owns the lock</param>
    public void ReleaseLock(string projectId, string jobId)
    {
        if (_projectStates.TryGetValue(projectId, out var state))
        {
            // Only remove if the job ID matches (prevent removing someone else's lock)
            if (state.JobId == jobId)
            {
                _projectStates.TryRemove(projectId, out _);
                _logger.LogDebug(
                    "Released indexing lock for project {ProjectId} (Job: {JobId})",
                    projectId,
                    jobId);
            }
            else
            {
                _logger.LogWarning(
                    "Attempted to release lock for project {ProjectId} with job {JobId}, " +
                    "but lock is held by job {ActualJobId}",
                    projectId,
                    jobId,
                    state.JobId);
            }
        }
    }

    /// <summary>
    /// Checks if a project is currently being indexed
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <returns>Tuple with (isIndexing, activeJobId)</returns>
    public (bool isIndexing, string? activeJobId) IsIndexing(string projectId)
    {
        if (_projectStates.TryGetValue(projectId, out var state))
        {
            return (true, state.JobId);
        }

        return (false, null);
    }

    /// <summary>
    /// Cancels the active indexing job for a project
    /// </summary>
    /// <param name="projectId">Project ID</param>
    /// <returns>True if a job was cancelled, false if no job was running</returns>
    public bool CancelIndexing(string projectId)
    {
        if (_projectStates.TryGetValue(projectId, out var state))
        {
            _logger.LogInformation(
                "Cancelling indexing job {JobId} for project {ProjectId}",
                state.JobId,
                projectId);
            state.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Represents the indexing state for a specific project
    /// </summary>
    private class ProjectIndexingState
    {
        private readonly CancellationTokenSource _cts;

        public ProjectIndexingState(string jobId)
        {
            JobId = jobId;
            _cts = new CancellationTokenSource();
        }

        public string JobId { get; }
        public CancellationToken CancellationToken => _cts.Token;

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }
    }
}
