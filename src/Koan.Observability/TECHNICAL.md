# Koan Observability Technical Contract

## Ownership and composition

`ObservabilityModule` is discovered because the package is referenced. During `AddKoan()` module registration it
reads the already-registered host configuration and environment and compiles one immutable internal
`ObservabilityPlan`. Registration never builds a temporary service provider and never keeps process-static state.

The plan is the sole owner of these decisions:

- requested activation and signal switches;
- trace sample rate;
- validated OTLP endpoint and optional headers;
- resource service name, version, and instance ID;
- the safe status text projected through startup and composition facts.

Core's `ObservabilityOptions` remains an inert configuration projection because Core and Web diagnostics consume it.
It does not activate OpenTelemetry and moving it into this functional leaf would invert the dependency boundary.

## Activation matrix

| Host decision | Koan-owned providers |
|---|---|
| `Enabled=false` | none |
| traces and metrics both disabled | none |
| Production with no OTLP endpoint | none |
| non-Production with at least one signal enabled | provider for each enabled signal |
| Production with an OTLP endpoint and at least one signal enabled | provider and OTLP exporter for each enabled signal |

An inactive package still binds the Core-owned options contract for diagnostics and reports its reason; it does not
register a `TracerProvider` or `MeterProvider`.

## Signal boundary

Tracing subscribes once to `Koan.*`, plus ASP.NET Core and `HttpClient` instrumentation. Sampling is
`ParentBasedSampler(TraceIdRatioBasedSampler(rate))`. Metrics subscribe once to `Koan.*` and add runtime
instrumentation. OpenTelemetry SDK wildcard matching is the general boundary: a new framework instrument whose name
starts with `Koan.` participates without a central catalog, model annotation, or package-specific registration.

One resource is shared by both providers. Its service name comes from the host application name, its version from the
entry assembly, and its instance ID from the machine name. A configured endpoint creates matching OTLP trace and
metric exporters; the same optional header string is applied to both.

OpenTelemetry only activates metric instruments when a reader/exporter exists. The package supplies the OTLP metric
exporter when an endpoint is configured. A development/test host without one may add any standard `MetricReader`; the
wildcard subscription then covers all `Koan.*` meters.

## Standard extension path

Applications compose application-owned sources, processors, readers, and exporters through
`services.AddOpenTelemetry().WithTracing(...)` or `.WithMetrics(...)`. OpenTelemetry's hosting extensions coalesce
those registrations into the same provider. Koan exposes no public pipeline builder, callback, marker, exporter
wrapper, or instrumentation registry.

Production activation intentionally depends only on the package's own validated OTLP endpoint. The module does not
reflect over or infer separately configured exporter registrations. An application that needs a Production topology
without a Koan OTLP destination owns that complete standard OpenTelemetry pipeline instead of relying on package
activation.

## Configuration and correction

The plan accepts hierarchical `Koan:Observability:*` configuration and the conventional single-underscore or
double-underscore environment forms. Endpoint and headers also fall back to `OTEL_EXPORTER_OTLP_ENDPOINT` and
`OTEL_EXPORTER_OTLP_HEADERS`.

Composition rejects:

- a boolean other than `true` or `false`;
- a non-finite sample rate or one outside inclusive range `0..1`;
- an endpoint that is not an absolute HTTP or HTTPS URI.

The framework wraps module-registration failures in `KoanBootException`; its inner failure and outer message retain
the exact key, received value, and correction. Invalid exporter configuration never waits until first export to fail.

## Diagnostics and security

The module report, startup log, and composition capability disclose only active state, signal switches, wildcard
boundary, and exporter kind (`none` or `otlp`). They never disclose the endpoint or headers. The package does not
inspect or redact user-created span tags, baggage, metric dimensions, or exporter payloads.

OpenTelemetry providers and exporters are host-owned singletons and are disposed with the host. Runtime signal work
uses SDK listeners selected at composition; Koan performs no per-operation discovery or service-provider lookup.

## Unsupported guarantees

Focused evidence proves reference activation, wildcard trace/metric subscription, Production and explicit-disable
inertness, corrective invalid configuration, and standard OpenTelemetry co-composition. It does not prove a live
collector, delivery, retries, backend querying/retention, logs, tail sampling, every framework instrument's semantic
quality, or arbitrary application instrumentation.
