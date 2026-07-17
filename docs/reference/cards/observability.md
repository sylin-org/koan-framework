---
type: REF
domain: observability
title: "Observability — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/observability.md
---

# Observability — pillar map

> One-screen map of the Observability pillar — the opt-in OpenTelemetry leaf. Full detail: [ARCH-0088](../../decisions/ARCH-0088-extract-koan-observability-package.md).

**What it does** — Referencing `Koan.Observability` is the whole switch: `ObservabilityModule.Register` wires an OpenTelemetry pipeline at boot — traces + metrics + OTLP export — with **zero** `Program.cs` code. Tracing adds the framework `ActivitySource`s (`AddSource("Koan.Core", "Koan.Data", "Koan.Communication", "Koan.Web")`) plus ASP.NET Core / HttpClient instrumentation; metrics add runtime instrumentation. The package is a leaf — `ObservabilityOptions` and the health/probe primitives stay in `Koan.Core` so referencing OTel is a pure addition that can't cycle ([ARCH-0088](../../decisions/ARCH-0088-extract-koan-observability-package.md)). In Production with **no** OTLP endpoint configured the pipeline disables itself (off-by-default in prod until you point it somewhere).

## The one canonical pattern

There is no call to write — add the package reference and configure the `Koan:Observability` section (or `OTEL_EXPORTER_OTLP_ENDPOINT`). The pipeline builds itself at boot.

```xml
<!-- Reference = Intent: this single line enables traces + metrics + OTLP export -->
<ProjectReference Include="..\..\src\Koan.Observability\Koan.Observability.csproj" />
```

```jsonc
// appsettings.json — point it at a collector; in Production this is what flips it on
{
  "Koan": {
    "Observability": {
      "Enabled": true,
      "Traces": { "Enabled": true, "SampleRate": 0.1 },
      "Metrics": { "Enabled": true },
      "Otlp": { "Endpoint": "http://localhost:4317" }
    }
  }
}
```

## ≤5 attributes you'll use

These are `ObservabilityOptions` keys under `Koan:Observability` (set via config, not attributes).

| Option | What it does |
|---|---|
| `Enabled` | Master switch for the whole pipeline (default `true` in dev; auto-`false` in Production when no OTLP endpoint is set). |
| `Traces.Enabled` / `Traces.SampleRate` | Toggle tracing and set the ratio-based sampler (`0.0`–`1.0`, clamped; `0.1` = 10% dev default) via a `ParentBasedSampler`. |
| `Metrics.Enabled` | Toggle the metrics pipeline (runtime instrumentation + OTLP). |
| `Otlp.Endpoint` | OTLP collector URI; also read from `OTEL:EXPORTER:OTLP:ENDPOINT`. Presence in prod is what enables export. |
| `Otlp.Headers` | Optional OTLP export headers; also read from `OTEL:EXPORTER:OTLP:HEADERS`. |

## The escape hatch

For tests or non-config wiring, call the public idempotent extension directly — it stacks with the auto-registrar without double-building the pipeline (a `KoanObservabilityMarker` sentinel guards it, so the second call is a no-op):

```csharp
services.AddKoanObservability(o =>
{
    o.Traces.SampleRate = 1.0;          // full sampling for a repro
    o.Otlp.Endpoint = "http://otel:4317";
});
```

Because the pipeline is built once at boot, a post-boot `configure` updates the options seen by readers but does **not** rebuild the already-wired pipeline — supply pipeline-affecting settings via configuration (ARCH-0088).

## The sample that shows it

[`samples/S5.Recs`](../../../samples/S5.Recs/README.md) — references `Koan.Observability` directly, so booting the app stands up the full OTel traces+metrics pipeline by Reference=Intent with no wiring code.
