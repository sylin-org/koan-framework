# S13.DocMind Refactoring Plan (Updated)

## Current Coverage Snapshot
- **Durable pipeline realised.** `DocumentProcessingWorker` advances the ledger through extraction, embeddings, vision, and synthesis services, persisting stage telemetry and refresh scheduling hooks that match the resilient orchestration in the proposal.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L36-L200】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L14-L165】
- **Entity-first ingestion complete.** Uploads deduplicate binaries, persist `SourceDocument` summaries, emit timeline events, and queue processing work without custom repositories, aligning with the entity-first mandate.【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L38-L155】【F:samples/S13.DocMind/Models/SourceDocument.cs†L12-L120】【F:samples/S13.DocMind/Models/DocumentProcessingEvent.cs†L13-L70】
- **Discovery dashboards partially live.** Cached `DocumentDiscoveryProjection` snapshots feed the dashboard and diagnostics endpoints, though the projection still issues full collection scans when changes occur.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L181】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L30-L115】
- **Model catalog surfaced.** Controllers expose catalog, installation, and provider metadata, but configuration edits remain client-side only, leaving `DocMindOptions` untouched.【F:samples/S13.DocMind/Controllers/ModelsController.cs†L27-L126】【F:samples/S13.DocMind/wwwroot/app/services/configurationService.js†L56-L105】【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L84】

## Delta Analysis & Trim Assessment

| Theme | Gap | Trim evaluation |
|-------|-----|-----------------|
| Manual analysis management | The proposal’s manual multi-document workflow is absent; Angular analysis screens still surface per-document `DocumentInsight` rows and legacy vocabulary.【F:samples/S13.DocMind/wwwroot/app/controllers/analysisController.js†L1-L158】【F:samples/S13.DocMind/wwwroot/app/views/analysis/list.html†L13-L124】 | Cannot trim. Manual session creation/editing is a flagship objective and prerequisite for workshops. |
| Front-end contract alignment | Dashboard, documents, and template flows rely on outdated field names (`file.state`, `documentTypeName`) and never consume the new summaries/timelines.【F:samples/S13.DocMind/wwwroot/app/services/documentService.js†L24-L130】【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L88-L125】 | Cannot trim. Mislabelled data undermines the new domain narrative and confuses users. |
| Prompting consistency | `InsightSynthesisService` operates per document with fallback snippets; the delimiter-based multi-document patterns are unused.【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L29-L170】【F:docs/chunks/S13-DocMind/Implementation_Examples_GDoc_Integration.md†L520-L599】 | Cannot trim. Manual sessions depend on reliable multi-document synthesis payloads. |
| Configuration lifecycle | UI sliders/toggles never persist to `DocMindOptions`, creating a false sense of control.【F:samples/S13.DocMind/wwwroot/app/controllers/configurationController.js†L12-L152】【F:samples/S13.DocMind/wwwroot/app/services/configurationService.js†L56-L105】 | Cannot trim. Workshops require real configuration feedback loops. |
| Discovery refresh strategy | Projection builder still scans entire collections; freshness metadata lacks drift signalling.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L181】 | Should keep. Scaling demos and dashboards depend on reliable freshness windows. |
| Domain packaging | Entities live inside the sample project but already follow the new shapes and MCP metadata.【F:samples/S13.DocMind/Models/SourceDocument.cs†L12-L120】【F:samples/S13.DocMind/S13.DocMind.csproj†L1-L120】 | Can defer. A dedicated domain assembly adds polish but is not blocking the core objectives. |
| Observability & CI | Registrar lacks OpenTelemetry exporters; integration coverage stops at harness tests.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L118】【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】 | Should keep. Ops stories remain fragile without telemetry and automated smoke suites. |

## Refactoring Plan

1. **Manual Analysis Sessions & Multi-Document Synthesis**
   - Model a `DocumentAnalysisSession` entity capturing selected document IDs, synthesis prompt revisions, and stored outputs.
   - Extend `InsightSynthesisService` (or a new coordinator) to accept multi-document requests and parse the delimiter-driven payloads from the proposal, emitting structured findings for sessions.【F:docs/chunks/S13-DocMind/Implementation_Examples_GDoc_Integration.md†L520-L599】
   - Add session-focused controller endpoints (list/view/create/update/publish) and regenerate OpenAPI/MCP manifests accordingly.
   - Rebuild Angular’s analysis area around session management (wizard for selecting documents/profiles, run/poll status, revision history) and remove the legacy per-insight grid.【F:samples/S13.DocMind/wwwroot/app/controllers/analysisController.js†L1-L158】

2. **Client Contract Realignment**
   - Regenerate TypeScript clients from the current OpenAPI spec; replace bespoke REST calls (`/analysis`, `/document-types`) with strongly typed SDKs.
   - Rename services, controllers, and views to the new vocabulary (e.g., `DocumentsController` → `SourceDocumentsController` in JS context, templates → profiles) and consume the `SourceDocument.Summary` fields for dashboard cards and detail panes.【F:samples/S13.DocMind/wwwroot/app/services/documentService.js†L24-L130】【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L13-L125】
   - Wire document detail and dashboard views to the `/timeline`, `/chunks`, and `/insights` endpoints so progress and highlights reflect real ledger telemetry.【F:samples/S13.DocMind/Controllers/DocumentsController.cs†L52-L170】

3. **Configuration & Boot Telemetry**
   - Introduce APIs that read/write `DocMindOptions` (storage, processing, AI) with validation mirroring the registrar, and persist edits (e.g., to Mongo or configuration store).
   - Update the Angular configuration module to load actual option snapshots, persist changes, and display registrar health/notes to close the feedback loop.【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L84】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L60-L122】
   - Surface model installation queue status within the same view using the existing `/models/installations` endpoints.【F:samples/S13.DocMind/Controllers/ModelsController.cs†L67-L126】

4. **Prompting & Template Alignment**
   - Build a shared prompt builder that stores delimiter templates and extraction schemas on `SemanticTypeProfile`, replacing ad-hoc string concatenation in synthesis/template services.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L18-L208】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】
   - Ensure manual analysis sessions and template galleries capture prompt revisions and parsed outputs to power previews/tests.

5. **Discovery & Diagnostics Hardening**
   - Refactor `DocumentDiscoveryProjectionBuilder` to compute deltas using change tokens or incremental aggregation, recording freshness timestamps and queue drift for the dashboard.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L181】
   - Enhance diagnostics APIs with projection freshness, vector status, and retry pacing thresholds so the dashboard highlights staleness before it impacts users.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L71-L165】

6. **Observability & CI Guardrails**
   - Wire OpenTelemetry tracing/metrics around worker stages, AI calls, and vector operations; document compose overrides and sampling defaults in README.
   - Automate HTTP-level tests for upload→manual session flows and CLI/MCP smoke scripts in CI to catch regression early.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】

## Delivery Order
1. Manual analysis sessions & multi-document synthesis.
2. Client contract realignment.
3. Configuration persistence & prompt builder alignment.
4. Discovery & diagnostics hardening.
5. Observability and CI guardrails.

Deferring the separate domain assembly keeps focus on the manual analysis experience, UI contract parity, and operational readiness that unlock the proposal’s primary outcomes.
