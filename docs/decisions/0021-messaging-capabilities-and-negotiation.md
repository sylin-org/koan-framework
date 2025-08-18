# 0021 — Messaging capabilities and framework negotiation

Status: Accepted

Context
- Sora.Mq aims for low-friction, semantic APIs (Send/On<T>) over multiple brokers.
- Not all providers support the same features (e.g., delayed delivery, DLQ shape, exact once, scheduling).
- We need a consistent way to declare provider capabilities and for the framework to adapt behavior safely.

Decision
- Introduce IMessagingCapabilities surfaced by each provider factory. Example flags:
  - DelayedDelivery (bool)
  - DeadLettering (bool)
  - Transactions (bool)
  - MaxMessageSizeKB (int)
  - MessageOrdering (None | Partition)
  - ScheduledEnqueue (bool)
  - PublisherConfirms (bool)
- Add a capability-aware negotiation layer in Sora.Mq.Core that:
  - Evaluates requested features (from options/attributes) against provider capabilities.
  - Applies the best implementation strategy or no-ops with a clear diagnostic when not supported.
  - Records the effective plan in a per-bus EffectiveMessagingPlan for diagnostics.
- Delay semantics:
  - Preferred: provider-native delayed delivery (RabbitMQ delayed exchange, ASB scheduled enqueue, etc.).
  - Fallbacks:
    - TTL + DLX pattern (approximate delays; coarse granularity) when supported.
    - Inline backoff (consumer-level retry with requeue=false and scheduled re-publish by a worker) as last resort.
  - When none available, delay requests are ignored and a warning is logged once per type/route.
- DLQ and retry:
  - Map generic RetryOptions (MaxAttempts, Backoff, FirstDelaySeconds, MaxDelaySeconds) and DlqOptions to provider constructs.
  - If DLQ unsupported, enforce MaxAttempts=1 and log capability limitation.
- Observability:
  - Health contributor factors capability mismatches into a Degraded state (vs Unhealthy for hard errors).
  - Emit metrics for retries, DLQs, and delay application mode (native/fallback/none).

Consequences
- Uniform DX across providers; safer behavior when features are missing.
- Clear, diagnosable runtime behavior via EffectiveMessagingPlan and logs.
- Slight complexity in core negotiation code, but localized and testable.

Examples
- A message decorated with [DelaySeconds(30)] on a broker without delayed delivery will:
  - Use TTL+DLX if supported; otherwise log a warning and deliver immediately.
- A bus configured with DLQ enabled on a provider without DLQ will:
  - Set MaxAttempts=1 and mark plan.DlqMode = Unsupported; readiness remains Healthy with a Degraded note.
