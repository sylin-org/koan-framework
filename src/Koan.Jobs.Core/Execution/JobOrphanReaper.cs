using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.BackgroundServices;
using Koan.Jobs.Options;
using Koan.Jobs.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Jobs.Execution;

/// <summary>
/// Periodically scans every registered job type for Running rows whose lease has lapsed, reverts
/// them to <see cref="Model.JobStatus.Queued"/>, and re-enqueues them. Catches the case where a
/// worker died mid-execution AFTER startup (boot recovery handles the cold-start case). The
/// dispatcher stamps and heartbeats <c>LeasedUntil</c> for the lifetime of the body, so a healthy
/// long-running job is never reaped; the lease only lapses if the process actually stopped.
/// Non-critical — a failed sweep just defers recovery to the next interval.
/// </summary>
internal sealed class JobOrphanReaper : KoanBackgroundServiceBase
{
    private readonly JobTypeRegistry _registry;
    private readonly IJobQueue _queue;
    private readonly JobsOptions _options;

    public JobOrphanReaper(
        JobTypeRegistry registry,
        IJobQueue queue,
        IOptions<JobsOptions> options,
        ILogger<JobOrphanReaper> logger,
        IConfiguration configuration)
        : base(logger, configuration)
    {
        _registry = registry;
        _queue = queue;
        _options = options.Value;
    }

    public override string Name => "Koan.Jobs.OrphanReaper";
    public override bool IsCritical => false;

    public override async Task ExecuteCore(CancellationToken cancellationToken)
    {
        var interval = _options.ReaperInterval > TimeSpan.Zero ? _options.ReaperInterval : TimeSpan.FromSeconds(30);
        Logger.LogInformation("Job orphan reaper started — interval: {Interval}, lease: {Lease}", interval, _options.LeaseDuration);

        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, cancellationToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                var now = DateTimeOffset.UtcNow;
                var recovered = await _registry.ReapOrphansAll(_queue, now, Logger, cancellationToken);
                if (recovered > 0)
                    Logger.LogInformation("Orphan reaper recovered {Count} stale Running job(s).", recovered);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Orphan reaper sweep failed; will retry next interval.");
            }
        }
    }
}
