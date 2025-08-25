namespace Sora.Core;

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