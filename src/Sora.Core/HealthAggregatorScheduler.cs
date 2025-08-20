using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sora.Core;

/// Hosted scheduler that invites contributors to publish status near TTL expiry.
/// - Operates in quantized windows (QuantizationWindow)
/// - Applies uniform jitter and coalesces probes within a bucket
internal sealed class HealthAggregatorScheduler : BackgroundService
{
    private readonly IHealthAggregator _agg;
    private readonly HealthAggregatorOptions _opt;
    private readonly ILogger<HealthAggregatorScheduler> _log;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastInvited = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new();
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    public HealthAggregatorScheduler(IHealthAggregator agg, HealthAggregatorOptions opt, ILogger<HealthAggregatorScheduler> log)
    { _agg = agg; _opt = opt; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("Health aggregator disabled; scheduler not running.");
            return;
        }

        var win = _opt.Scheduler.QuantizationWindow;
        if (win <= TimeSpan.Zero) win = TimeSpan.FromSeconds(2);

        _log.LogInformation("Health aggregator scheduler started. Window={Window} Jitter={Jitter}%", win, _opt.Scheduler.JitterPercent * 100);

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
                    _log.LogDebug("Probe broadcast for {Count} components.", due.Count);
                }
                else
                {
                    foreach (var c in due)
                    {
                        _agg.RequestProbe(ProbeReason.TtlExpiry, component: c, stoppingToken);
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
                _log.LogWarning(ex, "Health aggregator scheduler loop error");
            }
        }

        _log.LogInformation("Health aggregator scheduler stopped.");
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
