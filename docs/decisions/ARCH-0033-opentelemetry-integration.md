# 0033 — OpenTelemetry integration (tracing + metrics)

---
id: ARCH-0033
slug: ARCH-0033-opentelemetry-integration
domain: ARCH
status: Accepted
date: 2025-08-17
---


## Context

Sora needs a first-class, optional observability story that works across console, web, data, and messaging. We want sensible dev defaults and production-safe behavior without forcing a specific backend.

## Decision

- Provide a core bootstrap `AddSoraObservability(Action<ObservabilityOptions>?)` in `Sora.Core` which:
  - Attaches OpenTelemetry Resource with service name/version derived from the entry assembly.
  - Enables tracing and metrics when `Sora:Observability:Enabled` is true, or when OTLP env vars are provided.
  - Instruments ASP.NET Core (when present) and `HttpClient` automatically.
  - Uses a `ParentBased(TraceIdRatioBased)` sampler; default sample rate is 10% for dev.
  - Exports to OTLP when `Sora:Observability:Otlp:Endpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` is configured.
  - Includes runtime metrics; no process metrics by default to avoid extra dependency.

- Web pipeline adds a response header `Sora-Trace-Id` when an activity is present to aid correlation.

- Provide a well-known, lightweight snapshot endpoint for status (no live data dump):
  - Path: `/.well-known/sora/observability`
  - Payload: enabled flags, resource (service name/version/instance), traces (enabled, sample rate, exporter, currentTraceId), metrics (enabled, exporter), propagation and the `Sora-Trace-Id` response header name.
  - Exposure: available by default in Development. In Production it’s disabled unless `Sora:Web:ExposeObservabilitySnapshot=true`.
  - Security: never returns secrets (no headers/tokens). Endpoint string may be shown (safe); consider masking in highly sensitive environments.

## Configuration

- Appsettings:
  - `Sora:Observability:Enabled` (bool, default: true in dev)
  - `Sora:Observability:Traces:Enabled` (bool, default: true)
  - `Sora:Observability:Traces:SampleRate` (double 0..1, default: 0.1)
  - `Sora:Observability:Metrics:Enabled` (bool, default: true)
  - `Sora:Observability:Otlp:Endpoint` (string, e.g., http://localhost:4317)
  - `Sora:Observability:Otlp:Headers` (string, e.g., Authorization=Bearer ...)

- Environment variables respected:
  - `OTEL_EXPORTER_OTLP_ENDPOINT`
  - `OTEL_EXPORTER_OTLP_HEADERS`

## Usage

- Call `builder.Services.AddSoraObservability();` after `AddSora()` in Program.cs. No changes required for non-web apps.
- For web apps, ASP.NET Core and HttpClient are instrumented automatically when present.
- The response includes `Sora-Trace-Id` for correlation.
- Inspect status at `/.well-known/sora/observability` (dev on by default; prod must opt in via `Sora:Web:ExposeObservabilitySnapshot=true`).

## Notes

- If no OTLP endpoint is configured and the environment is Production, observability is disabled by default.
- We may later add minimal spans in Data and Messaging layers using `ActivitySource` (scoped and cheap), guarded by options.
