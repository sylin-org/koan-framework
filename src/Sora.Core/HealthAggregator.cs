using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sora.Core;

public enum HealthStatus { Healthy, Degraded, Unhealthy, Unknown }

public sealed record HealthSample(
    string Component,
    HealthStatus Status,
    string? Message,
    DateTimeOffset TimestampUtc,
    TimeSpan? Ttl,
    IReadOnlyDictionary<string, string>? Facts
);

public sealed record HealthSnapshot(
    HealthStatus Overall,
    IReadOnlyList<HealthSample> Components,
    DateTimeOffset AsOfUtc
);

public enum ProbeReason { Startup, Manual, TtlExpiry, StaleSnapshot, PolicyRefresh }

public sealed class ProbeRequestedEventArgs : EventArgs
{
    public string? Component { get; init; }
    public ProbeReason Reason { get; init; }
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTimeOffset? NotAfterUtc { get; init; }
}

public interface IHealthAggregator
{
    /// Health Aggregator contract
    /// - Per-component TTL: Only applied when a component explicitly supplies a TTL in Push(..., ttl).
    ///   If no TTL is provided, the entry does not expire on its own; overall staleness is driven by policy.
    /// - Probe workflow: Call RequestProbe() to invite contributors to publish status. This raises ProbeRequested.
    ///   Handlers should respond by calling Push(...). The event is fire-and-forget; no synchronous guarantees.
    /// - Idempotency: Repeated Push from the same component is an upsert of that component’s status.
    /// - Readiness: Overall readiness uses aggregator policy; do not fabricate per-component TTLs.

    event EventHandler<ProbeRequestedEventArgs>? ProbeRequested;

    /// Subscribe a handler for probe invitations targeted at a specific component.
    /// When RequestProbe(component: X) is called and a scoped subscription exists for X,
    /// the aggregator will invoke those handlers and will not raise the general ProbeRequested event.
    /// When RequestProbe(component: null) is called (broadcast), all scoped handlers are invoked,
    /// and the general ProbeRequested event is also raised to reach generic listeners.
    /// Returns an IDisposable to remove the subscription.
    IDisposable Subscribe(string component, Action<ProbeRequestedEventArgs> handler);

    void RequestProbe(ProbeReason reason = ProbeReason.Manual, string? component = null, CancellationToken ct = default);

    void Push(string component, HealthStatus status, string? message = null, TimeSpan? ttl = null, IReadOnlyDictionary<string, string>? facts = null);

    HealthSnapshot GetSnapshot();
}

internal sealed class HealthAggregator : IHealthAggregator
{
    private readonly HealthAggregatorOptions _options;
    private readonly ConcurrentDictionary<string, HealthSample> _samples = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _probeLock = new();
    private DateTimeOffset _lastSnapshotAt = DateTimeOffset.MinValue;
    private readonly ConcurrentDictionary<string, List<Action<ProbeRequestedEventArgs>>> _scopedHandlers = new(StringComparer.OrdinalIgnoreCase);

    public HealthAggregator(HealthAggregatorOptions options)
    {
        _options = options;
    }

    public event EventHandler<ProbeRequestedEventArgs>? ProbeRequested;

    public void RequestProbe(ProbeReason reason = ProbeReason.Manual, string? component = null, CancellationToken ct = default)
    {
        var args = new ProbeRequestedEventArgs
        {
            Component = component,
            Reason = reason,
            NotAfterUtc = DateTimeOffset.UtcNow + _options.Policy.SnapshotStalenessWindow
        };
        // Targeted: if scoped handlers exist for the component, invoke them and skip the general event
        bool invokedScoped = false;
        if (!string.IsNullOrWhiteSpace(component))
        {
            if (_scopedHandlers.TryGetValue(component!, out var list))
            {
                Action<ProbeRequestedEventArgs>[] snapshot;
                lock (list) snapshot = list.ToArray();
                if (snapshot.Length > 0)
                {
                    invokedScoped = true;
                    foreach (var h in snapshot)
                    {
                        try { h(args); } catch { /* isolate */ }
                    }
                }
            }
        }
        else
        {
            // Broadcast: invoke all scoped handlers
            var all = _scopedHandlers.Values.ToArray();
            foreach (var list in all)
            {
                Action<ProbeRequestedEventArgs>[] snapshot;
                lock (list) snapshot = list.ToArray();
                foreach (var h in snapshot)
                {
                    try { h(args); } catch { /* isolate */ }
                }
                invokedScoped = true;
            }
        }

        // General event: raise if no targeted handlers were invoked, or when broadcast to reach generic listeners
        if (!invokedScoped || component is null)
        {
            var handlers = ProbeRequested;
            if (handlers is not null)
            {
                foreach (EventHandler<ProbeRequestedEventArgs> handler in handlers.GetInvocationList())
                {
                    try { handler(this, args); } catch { /* isolate */ }
                }
            }
        }
    }

    public void Push(string component, HealthStatus status, string? message = null, TimeSpan? ttl = null, IReadOnlyDictionary<string, string>? facts = null)
    {
        var now = DateTimeOffset.UtcNow;
        // Clamp message length
        if (message is not null && message.Length > _options.Limits.MaxMessageLength)
            message = message.Substring(0, _options.Limits.MaxMessageLength);
        // Clamp facts count and size (rough, by concatenating pairs)
        if (facts is not null)
        {
            if (facts.Count > _options.Limits.MaxFactsCountPerComponent)
                facts = facts.Take(_options.Limits.MaxFactsCountPerComponent).ToDictionary(k => k.Key, v => v.Value);
            var approxBytes = facts.Sum(kv => (kv.Key?.Length ?? 0) + (kv.Value?.Length ?? 0));
            if (approxBytes > _options.Limits.MaxFactsBytesPerComponent)
            {
                // Trim until under budget
                var trimmed = new Dictionary<string, string>();
                foreach (var kv in facts)
                {
                    if ((trimmed.Sum(x => x.Key.Length + x.Value.Length) + kv.Key.Length + kv.Value.Length) > _options.Limits.MaxFactsBytesPerComponent)
                        break;
                    trimmed[kv.Key] = kv.Value;
                }
                facts = trimmed;
            }
        }
        // TTL is honored only if provided; clamp to bounds
        TimeSpan? effectiveTtl = ttl is null ? null :
            TimeSpan.FromMilliseconds(Math.Clamp(ttl.Value.TotalMilliseconds,
                _options.Ttl.MinTtl.TotalMilliseconds,
                _options.Ttl.MaxTtl.TotalMilliseconds));

        var sample = new HealthSample(component, status, message, now, effectiveTtl, facts);
        _samples.AddOrUpdate(component, sample, (_, _) => sample);
        _lastSnapshotAt = now;
    }

    public HealthSnapshot GetSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<HealthSample>();
        foreach (var kv in _samples.ToArray())
        {
            var s = kv.Value;
            if (s.Ttl is not null && (s.TimestampUtc + s.Ttl.Value) <= now)
            {
                // TTL elapsed: represent as Unknown; keep sample to compute overall
                s = s with { Status = HealthStatus.Unknown };
                _samples.TryUpdate(kv.Key, s, kv.Value);
            }
            list.Add(s);
        }

        // Overall: worst state; Unknown handling via policy
        HealthStatus overall = HealthStatus.Healthy;
        foreach (var s in list)
        {
            var status = s.Status;
            if (status == HealthStatus.Unknown)
            {
                var required = _options.Policy.RequiredComponents.Any(r => string.Equals(r, s.Component, StringComparison.OrdinalIgnoreCase));
                if (_options.Policy.TreatUnknownAsDegradedForRequired && required)
                    status = HealthStatus.Degraded;
                else
                    continue;
            }
            if ((int)status > (int)overall) overall = status;
        }

        return new HealthSnapshot(overall, list.OrderBy(x => x.Component).ToList(), now);
    }

    public IDisposable Subscribe(string component, Action<ProbeRequestedEventArgs> handler)
    {
        if (string.IsNullOrWhiteSpace(component)) throw new ArgumentException("Component is required", nameof(component));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var list = _scopedHandlers.GetOrAdd(component, _ => new List<Action<ProbeRequestedEventArgs>>());
        lock (list) list.Add(handler);
        return new Unsubscriber(_scopedHandlers, component, handler);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly ConcurrentDictionary<string, List<Action<ProbeRequestedEventArgs>>> _map;
        private readonly string _component;
        private readonly Action<ProbeRequestedEventArgs> _handler;
        private bool _disposed;
        public Unsubscriber(ConcurrentDictionary<string, List<Action<ProbeRequestedEventArgs>>> map, string component, Action<ProbeRequestedEventArgs> handler)
        { _map = map; _component = component; _handler = handler; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_map.TryGetValue(_component, out var list))
            {
                lock (list) { list.RemoveAll(h => ReferenceEquals(h, _handler)); }
            }
        }
    }
}
