---
name: koan-observability
description: Opt-in OpenTelemetry leaf (ARCH-0088) — referencing Koan.Observability auto-wires traces + metrics + OTLP export by Reference=Intent, with configuration-owned options and IHealthContributor self-reporting
pillar: observability
card: docs/reference/cards/observability.md
status: current
last_validated: 2026-06-18
---

# Koan Observability

## Trigger this skill when you see

- A reference to `Koan.Observability` (assembly `Sylin.Koan.Observability`)
- The `Koan:Observability` config section, `ObservabilityOptions`, or `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL:EXPORTER:OTLP:ENDPOINT`
- OpenTelemetry wiring — `AddOpenTelemetry().WithTracing/WithMetrics`, `AddSource("Koan.Core", ...)`, `AddRuntimeInstrumentation`, `AddOtlpExporter`, a `ParentBasedSampler`
- `IHealthContributor` / `IHealthAggregator` / `HealthReport` / `HealthState` — health and probe self-reporting
- "traces", "spans", "metrics", "OTLP", "OpenTelemetry", "collector", "sampler / sample rate", "telemetry off in prod", "health contributor", "readiness probe"

## Core principle

**Reference = Intent.** Telemetry is a **leaf package, not kernel weight**. `Koan.Core` carries no OpenTelemetry SDK — referencing `Koan.Observability` is the whole switch: its `ObservabilityModule` stands up traces + metrics + OTLP export from configuration with **zero** `Program.cs` code ([ARCH-0088](../../../docs/decisions/ARCH-0088-extract-koan-observability-package.md)). Tracing adds the framework `Koan.*` activity sources plus ASP.NET Core / HttpClient instrumentation; metrics add runtime instrumentation. `ObservabilityOptions` and the health/probe primitives stay in `Koan.Core`, while the functional pipeline stays in the leaf. In Production with **no** OTLP endpoint configured the pipeline disables itself.

<!-- validate -->
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;                               // IHealthContributor
using Koan.Core.Observability.Health;          // HealthReport, HealthState

// Reference Koan.Observability and configure Koan:Observability; no telemetry wiring belongs here.
// An IHealthContributor is discovered and bridged into the health aggregator automatically.
public sealed class FeedHealthContributor : IHealthContributor
{
    public string Name => "MyApp.Feed";
    public bool IsCritical => false;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        var backlog = await CurrentBacklog(ct);
        var data = new System.Collections.Generic.Dictionary<string, object?> { ["backlog"] = backlog };
        return backlog > 1000
            ? new HealthReport(Name, HealthState.Degraded, $"backlog {backlog} over budget", null, data)
            : new HealthReport(Name, HealthState.Healthy, $"backlog {backlog}", null, data);
    }

    private static Task<int> CurrentBacklog(CancellationToken ct) => Task.FromResult(0);
}
```

## Reference = Intent activation

| Add this | Effect |
|---|---|
| `Koan.Observability` (alone) | Its module runs at boot → OTel pipeline (traces + metrics + OTLP) built from `Koan:Observability` config. No `Program.cs` code. |
| `Koan:Observability:Otlp:Endpoint` (or `OTEL_EXPORTER_OTLP_ENDPOINT`) | Points export at a collector; in **Production** its presence is what flips the pipeline on (absent → auto-disabled). |
| `Koan:Observability:Traces:SampleRate` | Ratio sampler `0.0`–`1.0` (clamped), wrapped in a `ParentBasedSampler`. `0.1` = 10% dev default. |
| an `IHealthContributor` implementation | Auto-bridged into `IHealthAggregator` via the `HealthContributorsBridge` — pillars (e.g. `JobsHealthContributor`) self-report this way. Health/probes live in `Koan.Core`, no OTel needed. |

`ObservabilityOptions` keys (set via config under `Koan:Observability`, **not** attributes): `Enabled` (master switch — dev default `true`, auto-`false` in prod with no OTLP endpoint), `Traces.Enabled` / `Traces.SampleRate`, `Metrics.Enabled`, `Otlp.Endpoint`, `Otlp.Headers`.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `services.AddOpenTelemetry().WithTracing(...).WithMetrics(...)` hand-wired in `Program.cs` | Reference `Koan.Observability` — the registrar builds the pipeline (Reference = Intent). |
| Calling a manual telemetry-registration helper in `Program.cs` | Drop it — the referenced package owns one host pipeline from configuration. |
| Adding 5 `OpenTelemetry.*` `PackageReference`s to `Koan.Core` (or a 3-OTel-ref `Koan.Web`) | The kernel ships **no** OTel SDK ([ARCH-0088](../../../docs/decisions/ARCH-0088-extract-koan-observability-package.md)); telemetry is the `Koan.Observability` leaf only. |
| Moving `ObservabilityOptions` out of `Koan.Core` "to match the assembly" | It stays in Core (the leaf assembly keeps the `Koan.Core.Observability` namespace by design); relocating it cycles with `AppRuntime`. |
| A bespoke `IHostedService` polling `/healthz` to report status | Implement `IHealthContributor` — it's auto-bridged into the aggregator and surfaced on the well-known health endpoint. |
| Expecting traces in Production with no collector configured | The pipeline disables itself in prod with no `Otlp.Endpoint`; set it (or `OTEL_EXPORTER_OTLP_ENDPOINT`) to turn export on. |

## Escape hatches

- **Tests and custom settings**: supply the same `Koan:Observability` configuration used by production. Pipeline-affecting settings are configuration-owned and compiled once at boot ([ARCH-0088](../../../docs/decisions/ARCH-0088-extract-koan-observability-package.md)).
- **Emit your own spans/metrics**: create an `ActivitySource` / `Meter` and add the application source through standard OpenTelemetry configuration. Koan's own Core, Data, Communication, and Web sources are already included.
- **Per-signal toggles**: `Koan:Observability:Traces:Enabled` / `:Metrics:Enabled` flip a single signal; `Koan:Observability:Enabled` is the master kill switch.
- **OTLP headers / endpoint via env**: `OTEL:EXPORTER:OTLP:ENDPOINT` and `OTEL:EXPORTER:OTLP:HEADERS` are read as fallbacks to the `Otlp.Endpoint` / `Otlp.Headers` options.
- **Custom readiness**: implement `IHealthContributor` (`Name`, `IsCritical`, `Check → HealthReport`); for low-level control push directly via `IHealthAggregator.Push(component, status, ...)` and invite probes with `RequestProbe(...)`.

## See also

- [Reference card: observability.md](../../../docs/reference/cards/observability.md) — one-screen pillar map
- [ARCH-0088 — extract the Koan.Observability package](../../../docs/decisions/ARCH-0088-extract-koan-observability-package.md) — the leaf-extraction decision (why options stay in Core, the dead-ref cleanup, the no-cycle rule)
- [Koan.Observability package guide](../../../src/Koan.Observability/README.md) — Reference=Intent activation, configuration, and operator-facing behavior
