using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using S5.Recs.Models;

namespace S5.Recs.Services;

/// <summary>
/// Orchestrates partition-based import pipeline by creating ImportJob entities.
/// Workers poll these jobs and process them through the pipeline stages.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class ImportOrchestrator : IImportOrchestrator
{
    private readonly ILogger<ImportOrchestrator> _logger;

    public ImportOrchestrator(ILogger<ImportOrchestrator> logger)
    {
        _logger = logger;
    }

    public async Task<List<string>> QueueImportAsync(
        string source,
        string[] mediaTypeIds,
        ImportOptions options,
        CancellationToken ct)
    {
        // Resolve media types
        var mediaTypes = new List<MediaType>();

        if (mediaTypeIds.Length == 1 && mediaTypeIds[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            // Import all media types
            mediaTypes = (await MediaType.All(ct)).ToList();

            if (!mediaTypes.Any())
            {
                throw new InvalidOperationException("No MediaTypes found. Please seed reference data first.");
            }

            _logger.LogInformation(
                "Resolved 'all' to {Count} media types",
                mediaTypes.Count);
        }
        else
        {
            // Import specific media types
            foreach (var mediaTypeId in mediaTypeIds)
            {
                var results = await MediaType.Query(
                    mt => mt.Id == mediaTypeId || mt.Name.Equals(mediaTypeId, StringComparison.OrdinalIgnoreCase),
                    ct);
                var mediaType = results.FirstOrDefault();

                if (mediaType == null)
                {
                    throw new InvalidOperationException($"MediaType '{mediaTypeId}' not found");
                }

                mediaTypes.Add(mediaType);
            }
        }

        // Create import jobs (one per media type)
        var jobIds = new List<string>();

        foreach (var mediaType in mediaTypes)
        {
            var job = new ImportJob
            {
                JobId = Guid.NewGuid().ToString(),
                Source = source,
                MediaTypeId = mediaType.Id!,
                MediaTypeName = mediaType.Name,
                Limit = options.Limit,
                Overwrite = options.Overwrite,
                Status = ImportJobStatus.Running,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Save to "jobs-active" partition
            using (EntityContext.Partition("jobs-active"))
            {
                await job.Save(ct);
            }

            jobIds.Add(job.JobId);

            _logger.LogInformation(
                "Queued import job {JobId} for {Source}/{MediaType}",
                job.JobId, source, mediaType.Name);
        }

        return jobIds;
    }

    public async Task<ImportProgressResponse> GetProgressAsync(
        string[] jobIds,
        CancellationToken ct)
    {
        var jobProgressList = new List<JobProgress>();

        foreach (var jobId in jobIds)
        {
            // Try active partition first
            ImportJob? job;
            using (EntityContext.Partition("jobs-active"))
            {
                var activeJobs = await ImportJob.Query(j => j.JobId == jobId, ct);
                job = activeJobs.FirstOrDefault();
            }

            // If not in active, check default (completed/failed)
            if (job == null)
            {
                var defaultJobs = await ImportJob.Query(j => j.JobId == jobId, ct);
                job = defaultJobs.FirstOrDefault();
            }

            if (job == null)
            {
                _logger.LogWarning("Job {JobId} not found", jobId);
                continue;
            }

            // Count media in each partition for this job
            int inRaw, inQueue, completed;

            using (EntityContext.Partition("import-raw"))
            {
                inRaw = (int)await Media.Count.Where(m => m.ImportJobId == jobId, ct: ct);
            }

            using (EntityContext.Partition("vectorization-queue"))
            {
                inQueue = (int)await Media.Count.Where(m => m.ImportJobId == jobId, ct: ct);
            }

            // Count completed (in default partition with VectorizedAt set)
            completed = (int)await Media.Count.Where(m => m.ImportJobId == jobId && m.VectorizedAt != null, ct: ct);

            // Calculate progress
            var total = inRaw + inQueue + completed;
            var percentComplete = total > 0 ? Math.Round((double)completed / total * 100, 1) : 0;

            var progress = new JobProgress(
                JobId: job.JobId,
                Source: job.Source,
                MediaType: job.MediaTypeName,
                Status: job.Status.ToString(),
                CreatedAt: job.CreatedAt,
                StartedAt: job.StartedAt,
                CompletedAt: job.CompletedAt,
                Counts: new PartitionCounts(
                    InRaw: inRaw,
                    InQueue: inQueue,
                    Completed: completed,
                    PercentComplete: percentComplete
                ),
                Errors: job.Errors
            );

            jobProgressList.Add(progress);
        }

        return new ImportProgressResponse(jobProgressList);
    }
}
