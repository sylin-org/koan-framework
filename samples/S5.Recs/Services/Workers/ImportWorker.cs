using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S5.Recs.Infrastructure;
using S5.Recs.Models;
using S5.Recs.Providers;

namespace S5.Recs.Services.Workers;

/// <summary>
/// Background worker that polls ImportJob entities in "jobs-active" partition
/// and streams media from providers, staging items in "import-raw" partition.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class ImportWorker : BackgroundService
{
    private readonly IEnumerable<IMediaProvider> _providers;
    private readonly ILogger<ImportWorker> _logger;

    public ImportWorker(
        IEnumerable<IMediaProvider> providers,
        ILogger<ImportWorker> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ImportWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll for active import jobs
                ImportJob? job;
                using (EntityContext.Partition("jobs-active"))
                {
                    var jobs = await ImportJob.All(
                        new DataQueryOptions { PageSize = 1, Sort = "CreatedAt" },
                        stoppingToken);
                    job = jobs.FirstOrDefault();
                }

                if (job != null)
                {
                    await ProcessJobAsync(job, stoppingToken);
                }
                else
                {
                    // No jobs - wait before polling again
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ImportWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("ImportWorker stopped");
    }

    private async Task ProcessJobAsync(ImportJob job, CancellationToken ct)
    {
        try
        {
            job.StartedAt ??= DateTimeOffset.UtcNow;

            using (EntityContext.Partition("jobs-active"))
            {
                await job.Save(ct);
            }

            _logger.LogInformation(
                "Processing import job {JobId}: {Source}/{MediaType}",
                job.JobId, job.Source, job.MediaTypeName);

            // Resolve provider
            var provider = _providers.FirstOrDefault(p =>
                p.Code.Equals(job.Source, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                throw new InvalidOperationException($"Provider '{job.Source}' not found");
            }

            // Resolve media type
            var mediaTypes = await MediaType.Query(mt => mt.Id == job.MediaTypeId, ct);
            var mediaType = mediaTypes.FirstOrDefault();

            if (mediaType == null)
            {
                throw new InvalidOperationException($"MediaType '{job.MediaTypeId}' not found");
            }

            int totalFetched = 0;

            // Stream from provider
            await foreach (var batch in provider.FetchStreamAsync(
                mediaType,
                job.Limit ?? int.MaxValue,
                ct))
            {
                // Enrich with pipeline metadata
                foreach (var media in batch)
                {
                    media.ImportJobId = job.JobId;
                    media.ContentSignature = EmbeddingUtilities.ComputeContentSignature(media);
                    media.ImportedAt = DateTimeOffset.UtcNow;
                }

                // Save to "import-raw" partition
                using (EntityContext.Partition("import-raw"))
                {
                    await batch.Save(ct);
                }

                totalFetched += batch.Count;

                _logger.LogInformation(
                    "Job {JobId}: Staged {Count} media in import-raw partition (total: {Total})",
                    job.JobId, batch.Count, totalFetched);

                // Check limit
                if (job.Limit.HasValue && totalFetched >= job.Limit.Value)
                {
                    break;
                }
            }

            // Move job to default partition (completed)
            job.Status = ImportJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;

            await ImportJob.Copy(j => j.Id == job.Id)
                .From(partition: "jobs-active")
                .To(partition: null) // default
                .Run(ct);

            using (EntityContext.Partition("jobs-active"))
            {
                await ImportJob.Remove(job.Id!, ct);
            }

            _logger.LogInformation(
                "Job {JobId}: Import completed, {Count} media items staged for validation",
                job.JobId, totalFetched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed: {Error}", job.JobId, ex.Message);

            job.Status = ImportJobStatus.Failed;
            job.Errors.Add($"{DateTimeOffset.UtcNow:O}: {ex.Message}");
            job.CompletedAt = DateTimeOffset.UtcNow;

            // Move failed job to default partition for audit trail
            try
            {
                await ImportJob.Copy(j => j.Id == job.Id)
                    .From(partition: "jobs-active")
                    .To(partition: null)
                    .Run(ct);

                using (EntityContext.Partition("jobs-active"))
                {
                    await ImportJob.Remove(job.Id!, ct);
                }
            }
            catch (Exception moveEx)
            {
                _logger.LogError(moveEx, "Failed to move failed job {JobId} to default partition", job.JobId);
            }
        }
    }
}
