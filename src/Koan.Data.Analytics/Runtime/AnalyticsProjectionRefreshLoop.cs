using Koan.Data.Abstractions.Analytics;
using Koan.Data.Analytics.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Analytics.Runtime;

/// <summary>
/// The scheduled refresh loop: on boot it catches up every due projection (the "service was down" case),
/// then ticks — refreshing each projection whose cadence has elapsed. The materialization store is
/// per-host (the engine is single-writer per file), so every host refreshes its own derived store; no
/// cross-host claim is needed for a store that rebuilds from the record store anyway.
/// </summary>
internal sealed class AnalyticsProjectionRefreshLoop(
    IServiceProvider services,
    IOptions<AnalyticsOptions> options,
    ILogger<AnalyticsProjectionRefreshLoop> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTimeOffset> _lastSweep = new(StringComparer.Ordinal);
    private readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RefreshLoopEnabled) return;

        // Catch-up sweep: anything already due refreshes immediately, closing the "service was down" gap.
        await Sweep(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_tick, stoppingToken).ConfigureAwait(false);
                await Sweep(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                // A sweep failure is never fatal to the host: the materialization is derived state, and the
                // next tick retries. The failure is visible in logs; facts report staleness separately.
                logger.LogWarning(error, "analytics projection sweep failed; retrying next tick");
            }
        }
    }

    private async Task Sweep(CancellationToken ct)
    {
        foreach (var question in AnalyticsCatalog.All())
        {
            if (question.Projection?.Interval is not { } interval) continue;
            var last = await LastRefreshAsync(question, ct).ConfigureAwait(false)
                       ?? DateTimeOffset.MinValue;
            if (DateTimeOffset.UtcNow - last < interval) continue;
            if (_lastSweep.TryGetValue(question.Name, out var claimed) && DateTimeOffset.UtcNow - claimed < interval) continue;

            _lastSweep[question.Name] = DateTimeOffset.UtcNow;
            try
            {
                var receipt = await question.RefreshAsync(services, ct, "loop").ConfigureAwait(false);
                logger.LogInformation(
                    "analytics projection {Recipe} refreshed: {Rows} row(s) in {Duration}ms",
                    receipt.Recipe, receipt.RowCount,
                    (DateTimeOffset.UtcNow - receipt.RefreshedUtc).TotalMilliseconds);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Staleness is the labeled fallback; a failed refresh degrades freshness, never correctness.
                logger.LogWarning(error, "analytics projection {Recipe} refresh failed; serving will fall back to live compute", question.Name);
            }
        }
    }

    private async Task<DateTimeOffset?> LastRefreshAsync(AnalyticsQuestion question, CancellationToken ct)
    {
        var sink = services.GetService<IAnalyticsProjectionSink>();
        if (sink is null) return null;
        var state = await sink.ReadStateAsync(question.Name, ct).ConfigureAwait(false);
        return state?.LastRefreshUtc;
    }
}
