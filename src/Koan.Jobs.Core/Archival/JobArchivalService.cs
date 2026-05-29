using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Execution;
using Koan.Jobs.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Archival;

/// <summary>
/// Periodically removes terminal jobs older than the configured retention period (JOBS-0003). With
/// per-type job sets there is no unified collection to sweep, so this fans out over the job-type
/// registry, sweeping each type's set.
/// </summary>
internal sealed class JobArchivalService : Koan.Core.BackgroundServices.KoanBackgroundServiceBase
{
    private readonly JobTypeRegistry _registry;
    private readonly JobsOptions _options;

    public JobArchivalService(
        JobTypeRegistry registry,
        IOptions<JobsOptions> options,
        ILogger<JobArchivalService> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _registry = registry;
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

        var interval = policy.SweepInterval > TimeSpan.Zero ? policy.SweepInterval : TimeSpan.FromHours(6);
        Logger.LogInformation("Job archival started — retention: {Retention}, interval: {Interval}", policy.RetentionPeriod, interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, cancellationToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                var cutoff = DateTimeOffset.UtcNow - policy.RetentionPeriod;
                var removed = await _registry.SweepArchival(cutoff, Math.Max(1, policy.BatchSize), cancellationToken);
                if (removed > 0) Logger.LogInformation("Job archival removed {Count} expired job(s).", removed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { Logger.LogError(ex, "Job archival sweep failed"); }
        }
    }
}
