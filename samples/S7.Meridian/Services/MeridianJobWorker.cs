using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public sealed class MeridianJobWorker : BackgroundService
{
    private readonly IPipelineProcessor _processor;
    private readonly ILogger<MeridianJobWorker> _logger;

    public MeridianJobWorker(IPipelineProcessor processor, ILogger<MeridianJobWorker> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = $"meridian-worker-{Environment.MachineName}-{Guid.NewGuid():N}";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await ProcessingJob.TryClaimAnyAsync(workerId, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Claimed job {JobId} (status: {Status}, retryCount: {RetryCount}) for pipeline {PipelineId}", 
                    job.Id, job.Status, job.RetryCount, job.PipelineId);

                try
                {
                    await _processor.ProcessAsync(job, stoppingToken);
                    _logger.LogInformation("Completed job {JobId} for pipeline {PipelineId}.", job.Id, job.PipelineId);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Processing job {JobId} failed (attempt {RetryCount}).", job.Id, job.RetryCount + 1);
                    var now = DateTime.UtcNow;
                    job.RetryCount++;
                    job.LastError = ex.Message;
                    job.CompletedAt = now;
                    job.HeartbeatAt = now;
                    if (job.RetryCount > 3)
                    {
                        job.Status = JobStatus.Failed;
                        _logger.LogError("Job {JobId} marked as Failed after {RetryCount} attempts.", job.Id, job.RetryCount);
                    }
                    else
                    {
                        job.Status = JobStatus.Pending;
                        _logger.LogInformation("Job {JobId} will retry (attempt {RetryCount} of 4).", job.Id, job.RetryCount + 1);
                    }
                    await job.Save(stoppingToken).ConfigureAwait(false);

                    try
                    {
                        var pipeline = await DocumentPipeline.Get(job.PipelineId, stoppingToken).ConfigureAwait(false);
                        if (pipeline is not null)
                        {
                            pipeline.ProcessedDocuments = job.ProcessedDocuments;
                            pipeline.UpdatedAt = now;
                            if (job.Status == JobStatus.Failed)
                            {
                                pipeline.Status = PipelineStatus.Failed;
                                pipeline.CompletedAt = now;
                            }
                            else
                            {
                                pipeline.Status = PipelineStatus.Queued;
                            }

                            await pipeline.Save(stoppingToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception pipelineEx) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(pipelineEx, "Failed to update pipeline status after job {JobId} error.", job.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
