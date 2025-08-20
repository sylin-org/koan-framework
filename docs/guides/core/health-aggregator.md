# Health aggregator (push-first)

Sora’s Health Aggregator provides a simple, push-first model for module health while keeping complexity inside the service:

- Push-only TTL: a component’s entry expires only when that component explicitly supplies a TTL on push; no guessed TTLs.
- Probe invite: the aggregator exposes an event to invite contributors to publish their latest status.
- Aggregator-driven scheduling: when TTLs are used, the aggregator schedules probe invitations at the right time using quantization, jitter, and coalescing.
- Policy-based readiness: overall readiness comes from an aggregator policy and snapshot staleness, not fabricated per-component timeouts.

## Contract at a glance

- Event: `event EventHandler<ProbeRequestedEventArgs> ProbeRequested`
- Scoped subscribe: `IDisposable Subscribe(string component, Action<ProbeRequestedEventArgs> handler)`
- Trigger: `RequestProbe(ProbeReason reason = ProbeReason.Manual, string? component = null, CancellationToken ct = default)`
- Submit: `Push(string component, HealthStatus status, string? message = null, TimeSpan? ttl = null, IReadOnlyDictionary<string,string>? facts = null)`
- Snapshot: `HealthSnapshot GetSnapshot()` (cheap read; never does active IO)

### Event args

- `Component`: null means broadcast (all); non-null scopes to a logical component name.
- `Reason`: `Startup | Manual | TtlExpiry | StaleSnapshot | PolicyRefresh`.
- `CorrelationId`: `Guid` to correlate trigger → pushes.
- `NotAfter`: soft deadline by which contributors should attempt to publish.

### Status semantics

- `Healthy | Degraded | Unhealthy | Unknown`.
- Expiry on TTL: when a component’s TTL elapses, its status becomes `Unknown`. Readiness policy decides how `Unknown` affects the aggregate.
- Upsert behavior: repeated pushes from the same component overwrite the previous sample and reset TTL from the latest push.

## Scheduler behavior (complexity in service)

- Only components that supplied a TTL are scheduled for refresh.
- Coalescing + quantization
  - `QuantizationWindow`: future probe times are snapped to fixed-size buckets to batch work.
  - If several components land in the same bucket, the aggregator raises a single broadcast probe; otherwise a scoped probe.
- Jitter
  - Uniform random offset of ±(JitterPercent × baseLead), bounded by `JitterAbsoluteMin`, to avoid synchronized bursts across instances.
- Refresh lead
  - Schedule invitations slightly before TTL expiry using the smaller of `QuantizationWindow` and a percentage of TTL (e.g., 10%).
- Guardrails
  - Clamp TTLs within `[MinTtl, MaxTtl]`.
  - Debounce repeated triggers for the same component with `MinComponentGap`.
  - Split large buckets using `MaxComponentsPerBucket` and `MinInterBucketGap`.

## Readiness policy

- `SnapshotStalenessWindow`: if no fresh pushes have been observed within this window, treat the aggregate as stale.
- Required vs optional components: the policy can declare required components (e.g., `core`, `data`, `mq`).
- Unknown mapping: treat `Unknown` as degraded for required components while ignoring unknown for optional ones.

## Configuration (appsettings.json)

Bind from `Sora:Health:Aggregator:*`:

```json
{
  "Sora": {
    "Health": {
      "Aggregator": {
        "Enabled": true,
        "Scheduler": {
          "EnableTtlScheduling": true,
          "QuantizationWindow": "00:00:02",
          "JitterPercent": 0.10,
          "JitterAbsoluteMin": "00:00:00.100",
          "MinComponentGap": "00:00:01",
          "MaxComponentsPerBucket": 256,
          "MinInterBucketGap": "00:00:00.100",
          "BroadcastThreshold": 2,
          "RefreshLeadPercent": 0.10,
          "RefreshLeadAbsoluteMin": "00:00:00.100"
        },
        "Ttl": {
          "MinTtl": "00:00:01",
          "MaxTtl": "01:00:00"
        },
        "Policy": {
          "SnapshotStalenessWindow": "00:00:30",
          "TreatUnknownAsDegradedForRequired": true,
          "RequiredComponents": [ "core", "data", "mq" ],
          "OptionalComponents": [ "ai", "cache" ],
          "DegradedComponentsThreshold": 1
        },
        "Limits": {
          "MaxFactsBytesPerComponent": 4096,
          "MaxFactsCountPerComponent": 32,
          "MaxMessageLength": 512
        }
      }
    }
  }
}
```

Notes
- All timespans use `hh:mm:ss.fff` format.
- Per-component TTL is applied only when provided in `Push`.
- The aggregator never awaits event handlers; handlers must be safe and fast.

## Naming decisions (for contributors)

- Subscribe to: `ProbeRequested` or `Subscribe(component, handler)`
- Trigger an invite: `RequestProbe(...)`
- Submit status: `Push(...)`
- Read: `GetSnapshot()`
