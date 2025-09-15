# Cross-Cutting and Observability

Cross-cutting policies
- Logging, headers, and rate-limiters layered in web hosting (see ADR 0011).
- Security, validation, and error handling applied at edges.

Observability in Koan
- Use `AddKoanObservability` for tracing/metrics; configure OTLP exporter.
- `Koan-Trace-Id` response header correlates logs and traces.
- Snapshot endpoint: `/.well-known/Koan/observability` exposes safe runtime status.

Tips
- Start with a local OTEL collector (see `samples/S2.Compose/otel-collector-config.yaml`).
- Tag spans with domain-relevant attributes (aggregate name, command type) sparingly.

## Terms in plain language
- Cross-Cutting: concerns that apply everywhere (logging, security, error handling).
- Trace: a recorded path of a request through services.
- Span: a single step in a trace.
- Metric: a number tracked over time (e.g., request rate, error count).
