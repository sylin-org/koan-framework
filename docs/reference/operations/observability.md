---
type: REFERENCE
domain: operations
title: "Export Koan telemetry"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: optional OpenTelemetry composition and OTLP export
---

# Export Koan telemetry

Reference `Sylin.Koan.Observability` when the application needs one OpenTelemetry pipeline for Koan
trace sources and meters, ASP.NET Core and `HttpClient` traces, and runtime metrics. Keep the ordinary
`AddKoan()` call; the package contributes the pipeline.

```json
{
  "Koan": {
    "Observability": {
      "Traces": { "Enabled": true, "SampleRate": 0.1 },
      "Metrics": { "Enabled": true },
      "Otlp": { "Endpoint": "http://collector:4317" }
    }
  }
}
```

In Production, an OTLP endpoint is required for package activation. Outside Production the providers
may compose without one so tests or application-owned exporters can extend them. Invalid booleans,
sample rates, or endpoint URIs reject at boot and name the correction.

Use standard `services.AddOpenTelemetry()` calls to add application sources, processors, readers, or
exporters. Koan does not introduce another exporter abstraction. Runtime facts report activation,
signal switches, subscription, and exporter kind without exposing headers or credentials.

This capability does not promise log export, collector/backend health, delivery, retention, tail
sampling, application-specific spans, or safe tag cardinality for instruments it does not own.
