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

                try
                {
                    await _processor.ProcessAsync(job, stoppingToken);
                    job.Status = JobStatus.Completed;
                    job.CompletedAt = DateTime.UtcNow;
                    job.HeartbeatAt = DateTime.UtcNow;
                    await job.Save(stoppingToken);
                    _logger.LogInformation("Completed job {JobId} for pipeline {PipelineId}.", job.Id, job.PipelineId);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Processing job {JobId} failed.", job.Id);
                    job.RetryCount++;
                    job.LastError = ex.Message;
                    job.CompletedAt = DateTime.UtcNow;
                    job.HeartbeatAt = DateTime.UtcNow;
                    if (job.RetryCount > 3)
                    {
                        job.Status = JobStatus.Failed;
                    }
                    else
                    {
                        job.Status = JobStatus.Pending;
                    }
                    await job.Save(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
