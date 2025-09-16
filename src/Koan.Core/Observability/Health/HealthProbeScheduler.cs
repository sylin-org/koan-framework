using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.BackgroundServices;
using Koan.Core.Observability.Probes;
using System.Collections.Concurrent;

namespace Koan.Core.Observability.Health;

/// Hosted scheduler that invites contributors to publish status near TTL expiry.
/// - Operates in quantized windows (QuantizationWindow)
/// - Applies uniform jitter and coalesces probes within a bucket
[KoanBackgroundService(RunInProduction = true)]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Health.ProbeScheduled, EventArgsType = typeof(ProbeScheduledEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Health.ProbeBroadcast, EventArgsType = typeof(ProbeBroadcastEventArgs))]
internal sealed class HealthProbeScheduler : KoanFluentServiceBase
{
    private readonly IHealthAggregator _agg;
    private readonly HealthAggregatorOptions _opt;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastInvited = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new();
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    public HealthProbeScheduler(
        ILogger<HealthProbeScheduler> logger,
        IConfiguration configuration,
        IHealthAggregator agg,
        HealthAggregatorOptions opt,
        IHostApplicationLifetime lifetime)
        : base(logger, configuration)
    {
        _agg = agg;
        _opt = opt;
        _lifetime = lifetime;
    }

    public override async Task ExecuteCoreAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            Logger.LogInformation("Health aggregator disabled; scheduler not running.");
            return;
        }

        // Defer scheduler start until the application has signaled ApplicationStarted
        if (!_lifetime.ApplicationStarted.IsCancellationRequested)
        {
            Logger.LogDebug("Health aggregator scheduler waiting for application start...");
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStarted);
                await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { /* started */ }
        }

        var win = _opt.Scheduler.QuantizationWindow;
        if (win <= TimeSpan.Zero) win = TimeSpan.FromSeconds(2);

        Logger.LogInformation("Health aggregator scheduler started. Window={Window} Jitter={Jitter}%", win, _opt.Scheduler.JitterPercent * 100);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(win, stoppingToken);
                if (!_opt.Scheduler.EnableTtlScheduling) continue;

                var now = DateTimeOffset.UtcNow;
                var snap = _agg.GetSnapshot();
                var due = new List<string>();

                foreach (var s in snap.Components)
                {
                    if (s.Ttl is null) continue; // only TTL-driven components are scheduled

                    var ttl = s.Ttl.Value;
                    var baseLead = ComputeRefreshLead(ttl, _opt);
                    var jitter = ComputeJitter(baseLead, _opt);
                    var offsetMs = (jitter.TotalMilliseconds <= 0) ? 0 : (_rng.NextDouble() * 2.0 - 1.0) * jitter.TotalMilliseconds; // [-jitter, +jitter]
                    var inviteAt = s.TimestampUtc + ttl - baseLead + TimeSpan.FromMilliseconds(offsetMs);

                    // Quantize to window boundary (ceil)
                    var bucketTime = QuantizeCeil(inviteAt, win);

                    if (bucketTime <= now && AllowedByGap(s.Component, now))
                    {
                        due.Add(s.Component);
                    }
                }

                if (due.Count == 0) continue;

                // Coalesce
                if (due.Count >= _opt.Scheduler.BroadcastThreshold)
                {
                    _agg.RequestProbe(ProbeReason.TtlExpiry, component: null, stoppingToken);
                    Logger.LogDebug("Probe broadcast for {Count} components.", due.Count);
                    
                    await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Health.ProbeBroadcast, new ProbeBroadcastEventArgs
                    {
                        ComponentCount = due.Count,
                        BroadcastAt = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    foreach (var c in due)
                    {
                        _agg.RequestProbe(ProbeReason.TtlExpiry, component: c, stoppingToken);
                        
                        await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Health.ProbeScheduled, new ProbeScheduledEventArgs
                        {
                            Component = c,
                            Reason = "TTL Expiry",
                            ScheduledAt = DateTimeOffset.UtcNow
                        });
                    }
                }

                var stamp = DateTimeOffset.UtcNow;
                foreach (var c in due) _lastInvited[c] = stamp;

                // Backpressure: if too many, split over successive buckets by sleeping a minimal gap
                if (due.Count > _opt.Scheduler.MaxComponentsPerBucket)
                {
                    await Task.Delay(_opt.Scheduler.MinInterBucketGap, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Health aggregator scheduler loop error");
            }
        }

        Logger.LogInformation("Health aggregator scheduler stopped");
    }

    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Health.ForceProbe)]
    public async Task ForceProbeAction(string? component, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Manual health probe requested for component: {Component}", component ?? "ALL");

        _agg.RequestProbe(ProbeReason.Manual, component: component, cancellationToken);

        if (string.IsNullOrEmpty(component))
        {
            await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Health.ProbeBroadcast, new ProbeBroadcastEventArgs
            {
                ComponentCount = 0, // Unknown count for manual broadcast
                BroadcastAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Health.ProbeScheduled, new ProbeScheduledEventArgs
            {
                Component = component,
                Reason = "Manual",
                ScheduledAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static TimeSpan ComputeRefreshLead(TimeSpan ttl, HealthAggregatorOptions opt)
    {
        var percent = TimeSpan.FromMilliseconds(ttl.TotalMilliseconds * Math.Clamp(opt.Scheduler.RefreshLeadPercent, 0, 1));
        var lead = percent > opt.Scheduler.RefreshLeadAbsoluteMin ? percent : opt.Scheduler.RefreshLeadAbsoluteMin;
        // Don't exceed quantization window to keep batching effective
        if (opt.Scheduler.QuantizationWindow < lead) lead = opt.Scheduler.QuantizationWindow;
        return lead;
    }

    private static TimeSpan ComputeJitter(TimeSpan baseLead, HealthAggregatorOptions opt)
    {
        var pct = Math.Abs(opt.Scheduler.JitterPercent);
        var jitter = TimeSpan.FromMilliseconds(baseLead.TotalMilliseconds * pct);
        if (jitter < opt.Scheduler.JitterAbsoluteMin) jitter = opt.Scheduler.JitterAbsoluteMin;
        return jitter;
    }

    private static DateTimeOffset QuantizeCeil(DateTimeOffset t, TimeSpan window)
    {
        var ms = (t - Epoch).TotalMilliseconds / window.TotalMilliseconds;
        var ceil = Math.Ceiling(ms);
        return Epoch + TimeSpan.FromMilliseconds(ceil * window.TotalMilliseconds);
    }

    private bool AllowedByGap(string component, DateTimeOffset now)
    {
        if (!_lastInvited.TryGetValue(component, out var last)) return true;
        return (now - last) >= _opt.Scheduler.MinComponentGap;
    }
}
