# S13.DocMind Refactoring Plan (Updated)

## Current Coverage Snapshot
- **Durable pipeline core in place.** The hosted `DocumentProcessingWorker` advances `DocumentProcessingJob` ledgers through extraction, embeddings, vision, insights, and aggregation while emitting persisted telemetry, delivering the resilient orchestration envisioned in the proposal’s AI processing plan.【F:docs/chunks/S13-DocMind/03_ai_processing.md†L35-L66】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L418】【F:samples/S13.DocMind/Models/DocumentProcessingJob.cs†L15-L170】
- **Entity-first persistence enforced.** Repository helpers were removed in favour of `Entity<T>` access directly inside intake, diagnostics, discovery, and worker flows, ensuring projections, queue slices, and retries operate through the canonical static APIs recommended by the proposal.【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L78-L205】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L20-L214】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L52-L236】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L39-L139】
- **Operator visibility partially delivered.** Discovery refresh scheduling, vector bootstrap checks, and diagnostics endpoints expose queue slices, vector readiness, and replay scaffolding, giving operators insight into the live system but without the incremental and contract guardrails the proposal targets.【F:samples/S13.DocMind/Infrastructure/DocumentDiscoveryRefreshService.cs†L12-L173】【F:samples/S13.DocMind/Infrastructure/DocMindVectorHealth.cs†L8-L109】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L15-L210】

## Delta Analysis

### 1. Domain, storage, and client alignment
- Entities, value objects, and projections still live inside the sample web project with no dedicated domain assembly, migration scripts, or seed routines, leaving schema evolution disconnected from the blueprint’s phase-one expectations.【F:docs/chunks/S13-DocMind/06_implementation.md†L22-L44】【F:samples/S13.DocMind/Models/SourceDocument.cs†L1-L189】【F:samples/S13.DocMind/Models/DocumentProcessingJob.cs†L15-L206】
- DTOs and API contracts have not been regenerated for Angular or MCP consumers, so cross-channel alignment (UI, CLI, MCP) remains incomplete compared to the proposal’s contract synchronisation goals.【F:docs/chunks/S13-DocMind/06_implementation.md†L34-L44】【F:samples/S13.DocMind/Controllers/DocumentsController.cs†L1-L138】

### 2. AI prompting and semantic tooling
- Template generation and insight synthesis currently rely on ad-hoc prompts and JSON parsing instead of the delimiter-driven, schema-safe patterns captured from the GDoc reference (e.g., strict delimiter JSON for document type creation, consolidated analysis scaffolds), increasing the risk of brittle outputs.【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L10-L118】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L18-L208】【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L18-L220】
- Prompt variables and extraction schemas on `SemanticTypeProfile` are not yet wired into a reusable prompt-builder abstraction, so downstream services duplicate string manipulation instead of following the lean prompt architecture described in the proposal package.【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L120-L209】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】【F:samples/S13.DocMind/Services/TemplateGeneratorService.cs†L1-L78】

### 3. Discovery projections and diagnostics
- `DocumentDiscoveryProjectionBuilder` still performs full collection scans and emits coarse aggregates, missing the incremental `InsightCollection` projections, freshness windows, and lateness comparisons outlined in the data model blueprint.【F:docs/chunks/S13-DocMind/02_entity_models.md†L3-L122】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L18-L212】
- Diagnostics endpoints query live collections each time and lack cached queue/timeline comparisons, so operator experiences do not yet meet the proposal’s dashboard guardrails for latency and backlog tracking.【F:docs/chunks/S13-DocMind/01_executive_overview.md†L72-L95】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L54-L236】

### 4. Observability and operational automation
- Telemetry exporters, OpenTelemetry spans, and compose-ready instrumentation from the infrastructure plan remain unimplemented; `Program.cs` and the registrar only register Koan defaults and health checks, leaving traces/metrics absent from the promised dashboards.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L60-L109】【F:samples/S13.DocMind/Program.cs†L1-L9】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L19-L115】
- Operational scripts (reset tooling, compose overrides, env-var matrices) have not been produced, so bootstrapping and failover steps described in the proposal are still manual exercises.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L5-L32】【F:samples/S13.DocMind/docker-compose.yml†L1-L120】

### 5. Testing, contracts, and CI guardrails
- HTTP-level integration tests, OpenAPI/MCP diffing, and CI automation have not been wired; existing tests stop at harness-level checks and leave the full upload→completion path unverified over HTTP or MCP contracts.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L82】【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L1-L88】
- CLI smoke scripts and replay automation are scaffolded but not executed in CI, so the operational guarantees in the testing plan remain aspirational.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L61-L110】【F:samples/S13.DocMind.Tools/Program.cs†L1-L142】

## Refactoring Plan

1. **Domain packaging & migrations**
   - Create `S13.DocMind.Domain` (entities, value objects, configuration) and move models out of the sample project.
   - Introduce migration/seed routines for `SourceDocument`, `DocumentProcessingJob`, and related projections plus scripts to project legacy data into the new schema.
   - Regenerate API/TypeScript/MCP clients once contracts stabilise and document breaking changes for workshops.

2. **Prompting framework & semantic tooling alignment**
   - Implement a dedicated prompt builder that encapsulates the GDoc delimiter patterns (document type generation, consolidated analysis, lean prompts) and expose it through `TemplateGeneratorService`, `InsightSynthesisService`, and future multi-document flows.
   - Store canonical system/user prompts and extraction schemas in `SemanticTypeProfile`, wire variable substitution, and persist prompt revisions for auditing.
   - Update AI responses to emit the delimited payloads, add strict extractors, and expand testing with captured transcripts.

3. **Discovery projections & diagnostics**
   - Replace full scans with incremental `InsightCollection` projections, freshness metadata, and cached queue/timeline snapshots.
   - Extend diagnostics APIs to surface projection freshness, backlog drift, and retry pacing with thresholds matching the executive dashboard requirements.

4. **Observability & operations**
   - Wire OpenTelemetry tracing/metrics around worker stages, AI calls, and vector interactions; document compose overrides for the otel collector and sampling knobs.
   - Ship operational scripts (reset, smoke, profile toggles) and enrich the boot report with telemetry/exporter configuration per the infrastructure chapter.

5. **Testing & CI guardrails**
   - Add `WebApplicationFactory`-based integration tests covering upload→completion, template suggestions, and diagnostics endpoints; include vector fallback cases.
   - Automate OpenAPI/MCP diffing, CLI smoke runs, and replay workflows in CI so pipeline regressions surface immediately.

## Delivery Order
1. Domain packaging & migrations.
2. Prompting framework & semantic tooling alignment.
3. Discovery projections & diagnostics.
4. Observability & operations.
5. Testing & CI guardrails.
