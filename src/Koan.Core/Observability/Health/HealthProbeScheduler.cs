using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.BackgroundServices;
using Koan.Core.Logging;
using Koan.Core.Observability.Probes;

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
            KoanLog.HealthInfo(Logger, LogActions.Scheduler, LogOutcomes.Disabled);
            return;
        }

        // Defer scheduler start until the application has signaled ApplicationStarted
        if (!_lifetime.ApplicationStarted.IsCancellationRequested)
        {
            KoanLog.HealthDebug(Logger, LogActions.Scheduler, "waiting-start");
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStarted);
                await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { /* started */ }
        }

        var window = _opt.Scheduler.QuantizationWindow;
        if (window <= TimeSpan.Zero) window = TimeSpan.FromSeconds(2);

        KoanLog.HealthInfo(Logger, LogActions.Scheduler, "started",
            ("window", window),
            ("jitterPercent", _opt.Scheduler.JitterPercent * 100));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(window, stoppingToken);
                if (!_opt.Scheduler.EnableTtlScheduling) continue;

                var now = DateTimeOffset.UtcNow;
                var snapshot = _agg.GetSnapshot();
                var dueComponents = new List<string>();

                foreach (var component in snapshot.Components)
                {
                    if (component.Ttl is null) continue; // only TTL-driven components are scheduled

                    var ttl = component.Ttl.Value;
                    var baseLead = ComputeRefreshLead(ttl, _opt);
                    var jitter = ComputeJitter(baseLead, _opt);
                    var offsetMs = (jitter.TotalMilliseconds <= 0) ? 0 : (_rng.NextDouble() * 2.0 - 1.0) * jitter.TotalMilliseconds; // [-jitter, +jitter]
                    var inviteAt = component.TimestampUtc + ttl - baseLead + TimeSpan.FromMilliseconds(offsetMs);

                    // Quantize to window boundary (ceil)
                    var bucketTime = QuantizeCeil(inviteAt, window);

                    if (bucketTime <= now && AllowedByGap(component.Component, now))
                    {
                        dueComponents.Add(component.Component);
                    }
                }

                if (dueComponents.Count == 0) continue;

                // Coalesce
                if (dueComponents.Count >= _opt.Scheduler.BroadcastThreshold)
                {
                    _agg.RequestProbe(ProbeReason.TtlExpiry, component: null, stoppingToken);
                    KoanLog.HealthDebug(Logger, LogActions.Scheduler, "broadcast",
                        ("count", dueComponents.Count));
                    
                    await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Health.ProbeBroadcast, new ProbeBroadcastEventArgs
                    {
                        ComponentCount = dueComponents.Count,
                        BroadcastAt = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    foreach (var component in dueComponents)
                    {
                        _agg.RequestProbe(ProbeReason.TtlExpiry, component, stoppingToken);
                        
                        await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Health.ProbeScheduled, new ProbeScheduledEventArgs
                        {
                            Component = component,
                            Reason = "TTL Expiry",
                            ScheduledAt = DateTimeOffset.UtcNow
                        });
                    }
                }

                var stamp = DateTimeOffset.UtcNow;
                foreach (var component in dueComponents) _lastInvited[component] = stamp;

                // Backpressure: if too many, split over successive buckets by sleeping a minimal gap
                if (dueComponents.Count > _opt.Scheduler.MaxComponentsPerBucket)
                {
                    await Task.Delay(_opt.Scheduler.MinInterBucketGap, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                KoanLog.HealthWarning(Logger, LogActions.Scheduler, "loop-error",
                    ("error", ex.Message));
            }
        }
        KoanLog.HealthInfo(Logger, LogActions.Scheduler, "stopped");
    }

    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Health.ForceProbe)]
    public async Task ForceProbeAction(string? component, CancellationToken cancellationToken)
    {
        KoanLog.HealthInfo(Logger, LogActions.Scheduler, "manual-request",
            ("component", component ?? "ALL"));

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
        var refreshPercent = opt.Scheduler.RefreshLeadPercent;
        if (refreshPercent < 0)
        {
            refreshPercent = 0;
        }
        else if (refreshPercent > 1)
        {
            refreshPercent = 1;
        }

        var percent = TimeSpan.FromMilliseconds(ttl.TotalMilliseconds * refreshPercent);
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

    private static DateTimeOffset QuantizeCeil(DateTimeOffset value, TimeSpan window)
    {
        var ms = (value - Epoch).TotalMilliseconds / window.TotalMilliseconds;
        var ceil = Math.Ceiling(ms);
        return Epoch + TimeSpan.FromMilliseconds(ceil * window.TotalMilliseconds);
    }

    private bool AllowedByGap(string component, DateTimeOffset now)
    {
        if (!_lastInvited.TryGetValue(component, out var last)) return true;
        return (now - last) >= _opt.Scheduler.MinComponentGap;
    }


    private static class LogActions
    {
        public const string Scheduler = "health.scheduler";
    }

    private static class LogOutcomes
    {
        public const string Disabled = "disabled";
    }
}
