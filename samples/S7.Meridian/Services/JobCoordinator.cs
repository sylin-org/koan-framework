using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IJobCoordinator
{
    Task<ProcessingJob> ScheduleAsync(string pipelineId, IEnumerable<string> documentIds, CancellationToken ct);
}

public sealed class JobCoordinator : IJobCoordinator
{
    private readonly ILogger<JobCoordinator> _logger;

    public JobCoordinator(ILogger<JobCoordinator> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessingJob> ScheduleAsync(string pipelineId, IEnumerable<string> documentIds, CancellationToken ct)
    {
        var ids = documentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation("JobCoordinator.ScheduleAsync called for pipeline {PipelineId} with {Count} document IDs: {DocumentIds}",
            pipelineId, ids.Count, string.Join(", ", ids));

        if (ids.Count == 0)
        {
            throw new InvalidOperationException("Cannot schedule a job without documents.");
        }

        const int MaxRetries = 3;

        // Retry loop for optimistic concurrency
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var existing = await ProcessingJob.FindPendingAsync(pipelineId, ct);

            if (existing is not null)
            {
                // Optimistic concurrency: Capture version before modification
                var oldVersion = existing.Version;
                var beforeCount = existing.DocumentIds.Count;

                var merged = existing.MergeDocuments(ids);
                if (merged)
                {
                    var added = existing.DocumentIds.Count - beforeCount;
                    existing.Version++; // Increment version
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.HeartbeatAt = DateTime.UtcNow;

                    try
                    {
                        // Save with version check
                        var saved = await existing.Save(ct);
                        _logger.LogInformation("Appended {Added} documents to existing job {JobId} for pipeline {PipelineId} (attempt {Attempt}).",
                            added, existing.Id, pipelineId, attempt + 1);
                        return saved;
                    }
                    catch (Exception ex)
                    {
                        // Version conflict or other error - retry
                        _logger.LogDebug(ex, "Version conflict on job {JobId}, retrying ({Attempt}/{Max})",
                            existing.Id, attempt + 1, MaxRetries);

                        if (attempt < MaxRetries - 1)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), ct);
                            continue;
                        }

                        throw new InvalidOperationException(
                            $"Failed to merge documents into job {existing.Id} after {MaxRetries} attempts due to version conflicts.", ex);
                    }
                }
                else
                {
                    _logger.LogDebug("Reusing existing job {JobId} for pipeline {PipelineId}; no new documents detected.", existing.Id, pipelineId);
                    return existing;
                }
            }

            // No pending job exists, create new one
            try
            {
                var job = new ProcessingJob
                {
                    PipelineId = pipelineId,
                    Status = JobStatus.Pending,
                    Version = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    DocumentIds = ids,
                    TotalDocuments = ids.Count,
                    ProcessedDocuments = 0,
                    HeartbeatAt = DateTime.UtcNow
                };

                await job.Save(ct);
                _logger.LogInformation("Scheduled job {JobId} for pipeline {PipelineId} with {DocumentCount} documents.",
                    job.Id, pipelineId, job.DocumentIds.Count);
                return job;
            }
            catch (Exception ex)
            {
                // Another process may have created a job concurrently, retry
                _logger.LogDebug(ex, "Failed to create new job for pipeline {PipelineId}, retrying ({Attempt}/{Max})",
                    pipelineId, attempt + 1, MaxRetries);

                if (attempt < MaxRetries - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), ct);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Failed to schedule job for pipeline {pipelineId} after {MaxRetries} attempts.", ex);
            }
        }

        throw new InvalidOperationException($"Failed to schedule job for pipeline {pipelineId} after {MaxRetries} attempts.");
    }
}
