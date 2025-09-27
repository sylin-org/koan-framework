# S13.DocMind Refactoring Plan (Proposal Delta)

## Completion Snapshot
- **Estimated completion:** ~60% of the proposal scope. The stage-aware `DocumentProcessingWorker` now advances durable `DocumentProcessingJob` records through each processing phase while emitting events and resuming retries after restarts, giving DocMind the resilient workflow backbone described in the blueprint.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L200】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingJobRepository.cs†L12-L103】
- **Delivered capabilities:** Discovery refresh scheduling, vector bootstrap checks, and diagnostics endpoints expose queue snapshots, vector readiness, and replay flows so operators can observe the current pipeline even before the remaining proposal features land.【F:samples/S13.DocMind/Infrastructure/DocumentDiscoveryRefreshService.cs†L12-L173】【F:samples/S13.DocMind/Infrastructure/DocMindVectorHealth.cs†L8-L109】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L15-L210】

## Remaining Delta

### 1. Domain & client alignment
- The proposal calls for a dedicated `S13.DocMind.Domain` project plus migrations and seed scripts, yet all entities still live inside the web sample with no separate domain assembly or migration workflow, leaving storage shape and schema evolution out of sync with the blueprint expectations.【F:docs/chunks/S13-DocMind/06_implementation.md†L22-L26】【a7d3a3†L1-L6】【F:samples/S13.DocMind/Models/SourceDocument.cs†L1-L142】
- TypeScript/Angular client regeneration and DTO harmonisation have not happened, so API contracts remain backend-only and diverge from the cross-channel alignment the proposal requires for UI/MCP parity.【F:docs/chunks/S13-DocMind/06_implementation.md†L34-L44】

### 2. Discovery & analytics fidelity
- The discovery projection still performs full collection scans whenever a refresh runs and materialises ad-hoc aggregates instead of the planned `InsightCollection`/timeline projections, so analytics endpoints cannot scale or meet the dashboard guardrails defined in the data blueprint.【F:docs/chunks/S13-DocMind/02_entity_models.md†L6-L45】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L89】
- Queue/timeline diagnostics query live collections on every request, lacking the cached projections and lateness comparisons the proposal expects for reliable operator workflows.【F:docs/chunks/S13-DocMind/01_executive_overview.md†L78-L83】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L54-L133】

### 3. Observability & operational automation
- OpenTelemetry exporters, structured spans, and the compose-friendly instrumentation described in the infrastructure plan are missing; the current Program/registrar only wires Koan defaults and health checks, so no traces or metrics flow to the promised dashboards or otel-collector profile.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L109】【F:samples/S13.DocMind/Program.cs†L1-L9】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L115】
- Operational scripts and environment profiles such as the reset tooling, compose overrides, and documented environment variables have not been produced, leaving the delivery order’s operational hardening unaddressed.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L13】【7c37f0†L1-L5】

### 4. Testing & contract coverage
- The testing plan mandates HTTP-level integration tests, contract diffing, and CI automation, but the current coverage stops at in-memory harness tests with no WebApplicationFactory or contract verification, so regression and compatibility guarantees remain unmet.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L67】【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L1-L63】
- Release pipelines still lack the proposal’s OpenAPI/MCP diff checks and automated smoke scripts, leaving the sample without the repeatable quality gates required for go-live readiness.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L61-L82】

## Delivery Order
1. Domain & client alignment.
2. Discovery & analytics fidelity.
3. Observability & operational automation.
4. Testing & contract coverage.
