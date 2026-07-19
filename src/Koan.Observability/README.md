# Sylin.Koan.Observability

Reference `Sylin.Koan.Observability` when a Koan application should expose its framework traces and metrics through
OpenTelemetry.

## Install

```powershell
dotnet add package Sylin.Koan.Observability
```

The package composes through the application's existing `AddKoan()` call. There is no
`AddKoanObservability()` call: the package reference is the activation decision.

Outside Production, the reference creates one OpenTelemetry trace and metric pipeline with sensible development
defaults. In Production, configure an OTLP destination to activate it:

```jsonc
{
  "Koan": {
    "Observability": {
      "Traces": { "Enabled": true, "SampleRate": 0.1 },
      "Metrics": { "Enabled": true },
      "Otlp": {
        "Endpoint": "http://collector:4317"
      }
    }
  }
}
```

`OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS` are the equivalent environment variables. Headers
apply to both trace and metric export and are never included in startup or composition facts.

## Usage: observe Koan signals

- every `Koan.*` `ActivitySource` and `Meter`, including instruments added by future Koan packages;
- ASP.NET Core and `HttpClient` tracing;
- .NET runtime metrics;
- one resource identity derived from the host application and entry assembly;
- optional OTLP trace and metric exporters;
- startup and composition facts that explain activation, enabled signals, and exporter kind.

Use OpenTelemetry's standard builder for application sources, processors, readers, or another exporter. It composes
into the same providers:

```csharp
builder.Services
    .AddOpenTelemetry()
    .WithTracing(traces => traces.AddSource("Orders.*"));

builder.Services.AddKoan();
```

No Koan-specific source registry or exporter abstraction is required.

## Configuration

| Setting | Default | Effect |
|---|---:|---|
| `Koan:Observability:Enabled` | `true` | Master switch. `false` creates no Koan-owned provider. |
| `Koan:Observability:Traces:Enabled` | `true` | Enables Koan, ASP.NET Core, and `HttpClient` tracing. |
| `Koan:Observability:Traces:SampleRate` | `0.1` | Parent-based ratio sampling from `0` through `1`. |
| `Koan:Observability:Metrics:Enabled` | `true` | Enables Koan and runtime metric composition. |
| `Koan:Observability:Otlp:Endpoint` | none | Absolute HTTP(S) OTLP endpoint; required for package activation in Production. |
| `Koan:Observability:Otlp:Headers` | none | Optional OTLP header string applied to both exporters. |

Invalid booleans, sample rates, or endpoints reject host composition and name the exact setting plus its correction.

## Inspection and boundaries

The module boot report and shared composition facts state whether the package is active, which signals are enabled,
and whether export is `otlp` or `none`. Core health and Web diagnostic primitives remain in `Sylin.Koan.Core`; this
package owns only the optional OpenTelemetry SDK pipeline.

- Production without an OTLP endpoint is deliberately inert, even when application code separately configures a
  standard custom exporter. Applications with that topology should own the complete standard OpenTelemetry pipeline.
- Without an exporter or metric reader, development composition exports nothing; a standard reader/exporter can be
  added for local inspection or tests.
- The package does not provide log export, collector/backend availability, delivery, retention, tail sampling,
  application-specific spans, tag redaction, or custom-cardinality limits.
- Individual instrument owners remain responsible for safe values and bounded metric dimensions.

See [TECHNICAL.md](TECHNICAL.md) for the composition and failure contract and the
[Observability reference](../../docs/reference/cards/observability.md) for the compact operator map.
