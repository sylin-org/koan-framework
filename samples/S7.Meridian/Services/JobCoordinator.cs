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
        var job = new ProcessingJob
        {
            PipelineId = pipelineId,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            DocumentIds = documentIds.Distinct().ToList()
        };

        await job.Save(ct);
        _logger.LogInformation("Scheduled job {JobId} for pipeline {PipelineId} with {DocumentCount} documents.", job.Id, pipelineId, job.DocumentIds.Count);
        return job;
    }
}
