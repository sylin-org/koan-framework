using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Options;
using Koan.Jobs.Store;
using Koan.Jobs.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Archival;

/// <summary>
/// Background service that periodically removes completed and failed jobs
/// older than the configured retention period.
/// </summary>
internal sealed class JobArchivalService : Koan.Core.BackgroundServices.KoanBackgroundServiceBase
{
    private readonly IJobStoreResolver _resolver;
    private readonly JobIndexCache _index;
    private readonly JobsOptions _options;

    public JobArchivalService(
        IJobStoreResolver resolver,
        JobIndexCache index,
        IOptions<JobsOptions> options,
        ILogger<JobArchivalService> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _resolver = resolver;
        _index = index;
        _options = options.Value;
    }

    public override string Name => "Koan.Jobs.Archival";
    public override bool IsCritical => false;

    public override async Task ExecuteCore(CancellationToken cancellationToken)
    {
        var policy = _options.Archival;
        if (!policy.Enabled)
        {
            Logger.LogInformation("Job archival service disabled");
            return;
        }

        if (policy.RetentionPeriod <= TimeSpan.Zero)
        {
            Logger.LogWarning("Job archival retention period is <= 0; service will not run");
            return;
        }

        var interval = policy.SweepInterval > TimeSpan.Zero
            ? policy.SweepInterval
            : TimeSpan.FromHours(6);

        Logger.LogInformation(
            "Job archival service started — retention: {Retention}, batch: {BatchSize}, interval: {Interval}",
            policy.RetentionPeriod, policy.BatchSize, interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var removed = await SweepExpiredJobs(policy, cancellationToken);
                if (removed > 0)
                {
                    Logger.LogInformation("Job archival sweep removed {Count} expired jobs", removed);
                }
                else
                {
                    Logger.LogDebug("Job archival sweep found no expired jobs");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Job archival sweep failed");
            }
        }
    }

    private async Task<int> SweepExpiredJobs(JobArchivalPolicy policy, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - policy.RetentionPeriod;
        var batchSize = Math.Max(1, policy.BatchSize);
        var totalRemoved = 0;

        // Sweep jobs tracked in the index that are terminal and older than cutoff
        var expiredEntries = _index.GetAll()
            .Take(batchSize)
            .ToList();

        foreach (var entry in expiredEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var store = _resolver.Resolve(entry.StorageMode);
            var metadata = new JobStoreMetadata(
                entry.StorageMode, entry.Source, entry.Partition, entry.AuditEnabled, _options.SerializerOptions);

            var job = await store.Get(entry.JobId, metadata, cancellationToken);
            if (job == null)
            {
                // Job already gone — clean up index
                _index.Remove(entry.JobId);
                continue;
            }

            if (job.Status is not (JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled))
                continue;

            if (job.CompletedAt is null || job.CompletedAt > cutoff)
                continue;

            await store.Remove(entry.JobId, metadata, cancellationToken);
            totalRemoved++;

            if (totalRemoved >= batchSize)
                break;
        }

        return totalRemoved;
    }
}
