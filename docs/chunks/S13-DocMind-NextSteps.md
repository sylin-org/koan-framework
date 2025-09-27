# S13.DocMind Next Steps (Proposal Delta)

## Domain & Client Alignment
1. **Stand up the domain assembly and migrations.** Create the `S13.DocMind.Domain` project, move entity/value-object definitions out of the web sample, and add bootstrap/migration routines that project legacy `files` data into the new schema outlined in the proposal.【F:docs/chunks/S13-DocMind/06_implementation.md†L22-L26】【F:samples/S13.DocMind/Models/SourceDocument.cs†L1-L142】
2. **Regenerate contracts for UI and MCP clients.** Run `dotnet koan client` (and matching MCP tooling) once the domain DTOs stabilise so Angular/TypeScript consumers match the new API surface, documenting any breaking changes for workshop scripts.【F:docs/chunks/S13-DocMind/06_implementation.md†L34-L44】

## Discovery & Analytics Fidelity
1. **Implement incremental projections.** Replace the full-scan `DocumentDiscoveryProjectionBuilder` with targeted aggregates (`InsightCollection`, queue snapshots) that track deltas and persist freshness metadata for validation endpoints.【F:docs/chunks/S13-DocMind/02_entity_models.md†L6-L45】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L89】
2. **Add drift diagnostics and guardrails.** Extend diagnostics endpoints to compare cached projections with live counts/latency so operators can detect backlog or staleness before dashboards go stale.【F:docs/chunks/S13-DocMind/01_executive_overview.md†L78-L83】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L54-L133】

## Observability & Operational Automation
1. **Wire OpenTelemetry and compose instrumentation.** Add tracing/metrics exporters, wrap worker stages with spans, and document the otel-collector compose profile so dashboards from the infrastructure plan can light up.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L109】【F:samples/S13.DocMind/Program.cs†L1-L9】
2. **Publish operational scripts and profiles.** Deliver the reset script, compose overrides, and environment variable documentation promised in the infrastructure plan so teams can bootstrap, disable features, or reset data reliably.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L13】

## Testing & Contract Coverage
1. **Add HTTP-level integration and contract tests.** Introduce `WebApplicationFactory`-based tests that exercise upload→completion via HTTP, generate OpenAPI/MCP manifests in CI, and diff them against the committed baseline.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L67】
2. **Automate pipelines and smoke scripts.** Update CI to run unit/integration suites, CLI smoke commands, and artifact publication so the release checklist’s quality gates become repeatable automation instead of manual steps.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L61-L82】

## Immediate Spikes
- **Domain migration design:** Prototype how existing Mongo collections migrate into the proposed `SourceDocument`/`DocumentProcessingJob` schema without downtime.【F:docs/chunks/S13-DocMind/06_implementation.md†L22-L26】
- **Projection workload modelling:** Capture metrics from a representative dataset to size incremental discovery projections and validate refresh thresholds before implementation.【F:docs/chunks/S13-DocMind/02_entity_models.md†L6-L45】
- **Telemetry exporter selection:** Experiment with Koan’s OpenTelemetry hooks locally to choose the exporter stack (OTLP, console, or Prometheus) that best matches the compose environment.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L109】
