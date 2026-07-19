---
type: REF
domain: observability
title: "Observability — pillar map"
audience: [developers, operators, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: source-first
validation:
  date_last_tested: 2026-07-18
  status: verified
  scope: Koan.Observability focused reference-intent suite 8/8
---

# Observability — pillar map

> One-screen map of the optional OpenTelemetry capability. Full contracts:
> [package README](../../../src/Koan.Observability/README.md),
> [technical contract](../../../src/Koan.Observability/TECHNICAL.md), and
> [ARCH-0088](../../decisions/ARCH-0088-extract-koan-observability-package.md).

**What it does** — Reference `Sylin.Koan.Observability` and keep the application's ordinary `AddKoan()` call. The
module composes one OpenTelemetry pipeline for every `Koan.*` trace source and meter, ASP.NET Core and `HttpClient`
traces, and runtime metrics. A configured OTLP endpoint exports both signals with the same optional headers. Core
health and diagnostic primitives remain available independently in `Sylin.Koan.Core`.

## The canonical pattern

```powershell
dotnet add package Sylin.Koan.Observability
```

```jsonc
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

There is no Observability-specific `Program.cs` call. In Production the endpoint is required for package activation;
outside Production the providers compose without one so tests and local exporters can extend them.

## Operational decisions

| Decision | Meaning |
|---|---|
| package absent | no OpenTelemetry SDK dependency or Koan-owned provider |
| package present, non-Production | enabled signals compose; export occurs only when an exporter/reader exists |
| package present, Production, no endpoint | deliberately inert |
| `Enabled=false` | deliberately inert in every environment |
| `Traces.SampleRate` | parent-based ratio sampling, inclusive `0..1` |
| OTLP endpoint + headers | shared by trace and metric exporters; values remain redacted from facts |

Inspect the module boot report or shared composition facts for active state, signal switches, `Koan.*` subscription,
and exporter kind. Invalid booleans, sample rates, or endpoint URIs reject boot and name the exact correction.

## Standard extension path

Use `services.AddOpenTelemetry()` to add application sources, processors, readers, or exporters. Those registrations
coalesce with Koan's providers. There is no Koan callback, marker, source catalog, or exporter abstraction.

The package does not promise log export, delivery, collector/backend health, retention, tail sampling,
application-specific spans, tag redaction, or safe cardinality for instruments it does not own.
