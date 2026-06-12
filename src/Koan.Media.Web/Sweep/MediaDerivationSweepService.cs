using Koan.Media.Web.Options;
using Koan.Media.Web.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Media.Web.Sweep;

/// <summary>
/// Scheduled task that reclaims derivations whose source is gone. Implements
/// MEDIA-0007 §d: a periodic sweep replaces the legacy filesystem cache's
/// implicit "never evicted" disposal model.
///
/// <para>The service delegates the actual storage walk to
/// <see cref="IMediaSource.SweepOrphanedDerivationsAsync"/> — the framework
/// has no opinion on which entity types persist derivations, but supplies the
/// scheduling, cancellation, and structured logging substrate.</para>
///
/// <para>Disabled by default; opt in via
/// <c>Koan:Media:Web:DerivationSweep:Enabled = true</c>. The service can be
/// resolved from DI and its <see cref="RunOnceAsync"/> invoked manually after
/// bulk source deletes.</para>
/// </summary>
public sealed class MediaDerivationSweepService : BackgroundService
{
    private readonly IMediaSource _source;
    private readonly IOptionsMonitor<MediaWebOptions> _options;
    private readonly ILogger<MediaDerivationSweepService> _logger;

    public MediaDerivationSweepService(
        IMediaSource source,
        IOptionsMonitor<MediaWebOptions> options,
        ILogger<MediaDerivationSweepService> logger)
    {
        _source = source;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Execute one sweep pass synchronously. Intended for callers that want to
    /// trigger reclamation immediately (e.g. after a bulk source delete) without
    /// waiting for the scheduled cadence. Safe to call concurrently with the
    /// background loop; the sweep is idempotent.
    /// </summary>
    public async Task<MediaDerivationSweepResult> RunOnceAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _source.SweepOrphanedDerivationsAsync(ct).ConfigureAwait(false);
            if (result.Examined > 0 || result.Deleted > 0)
            {
                _logger.LogInformation(
                    "Media derivation sweep examined {Examined} row(s), deleted {Deleted} orphan(s).",
                    result.Examined, result.Deleted);
            }
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media derivation sweep failed; will retry on next cadence.");
            return MediaDerivationSweepResult.Empty;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue.DerivationSweep;
        if (!opts.Enabled)
        {
            _logger.LogDebug(
                "MediaDerivationSweepService is disabled (Koan:Media:Web:DerivationSweep:Enabled=false); idling.");
            return;
        }

        try
        {
            if (opts.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(opts.InitialDelay, stoppingToken).ConfigureAwait(false);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);

                var interval = _options.CurrentValue.DerivationSweep.Interval;
                if (interval <= TimeSpan.Zero)
                {
                    // Defensive: never busy-loop. If the operator set a zero
                    // interval, fall back to the default cadence.
                    interval = TimeSpan.FromHours(1);
                }
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
    }
}
