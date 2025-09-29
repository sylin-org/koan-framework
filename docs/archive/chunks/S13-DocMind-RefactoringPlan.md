# S13.DocMind Refactoring Plan (Updated)

## Current Coverage Snapshot
- **Durable pipeline realised.** `DocumentProcessingWorker` advances the ledger through extraction, embeddings, vision, synthesis, and aggregation stages while emitting diagnostics snapshots that the processing API surfaces for operators.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L774】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L14-L198】
- **Entity-first ingestion complete.** Upload flows deduplicate binaries, persist `SourceDocument` summaries, emit timeline events, and enqueue work via `DocumentProcessingJobQueries`, keeping repository logic inside the entity layer.【F:samples/S13.DocMind/Services/DocumentIntakeService.cs†L38-L195】【F:samples/S13.DocMind/Models/SourceDocument.cs†L12-L142】【F:samples/S13.DocMind/Models/DocumentProcessingEvent.cs†L13-L114】
- **Manual sessions wired end-to-end.** The `ManualAnalysisSession` entity, service, and Angular controllers deliver CRUD + run flows that call `InsightSynthesisService.GenerateManualSessionAsync`, persist synthesis history, and render the new manual analysis workspace.【F:samples/S13.DocMind/Models/ManualAnalysisSession.cs†L16-L188】【F:samples/S13.DocMind/Services/ManualAnalysisService.cs†L41-L163】【F:samples/S13.DocMind/wwwroot/app/controllers/analysisController.js†L1-L181】
- **Discovery dashboards partially live.** Cached `DocumentDiscoveryProjection` snapshots power dashboard and diagnostics views, although refreshes still read the full `SourceDocument` and `DocumentInsight` collections on every change.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L30-L144】
- **Model catalog surfaced.** Controllers expose catalog, installation, and provider metadata, but the configuration UI still mocks persistence, leaving `DocMindOptions` server-side only.【F:samples/S13.DocMind/Controllers/ModelsController.cs†L27-L126】【F:samples/S13.DocMind/wwwroot/app/services/configurationService.js†L56-L146】【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L103】

## Delta Analysis & Trim Assessment

| Theme | Gap | Trim evaluation |
|-------|-----|-----------------|
| Prompting toolkit alignment | Template generation prompts ignore the strict delimiter + JSON contract from the proposal, and manual synthesis builds bespoke prompt strings instead of the structured builder pattern, risking drift across services.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L36-L223】【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L359-L705】【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L21-L196】 | Cannot trim. Reliable document-type generation and manual synthesis depend on the documented delimiters and lean prompt architecture. |
| Front-end contract alignment | Dashboard, documents, and template flows still rely on legacy field names (`documentTypeName`, `doc.name`, `file.state`) and skip the new summaries/timelines emitted by the API.【F:samples/S13.DocMind/wwwroot/app/services/documentService.js†L1-L143】【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L88-L132】 | Cannot trim. Misaligned DTOs erode the new domain vocabulary and obscure processing telemetry. |
| Configuration lifecycle | UI sliders/toggles never persist to `DocMindOptions`, creating a false sense of control and hiding registrar validation feedback.【F:samples/S13.DocMind/wwwroot/app/controllers/configurationController.js†L12-L152】【F:samples/S13.DocMind/wwwroot/app/services/configurationService.js†L56-L146】【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L103】 | Cannot trim. Workshops require genuine configuration feedback loops. |
| Discovery refresh strategy | `DocumentDiscoveryProjectionBuilder` still issues whole-collection scans and omits freshness drift metadata, leaving dashboards without incremental updates.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】 | Should keep. Scaling demos and dashboards depend on reliable freshness windows. |
| Observability & CI | Registrar wires health checks but no OpenTelemetry exporters, and integration coverage stops at the harness tests with no HTTP/MCP smoke flows.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】 | Should keep. Ops stories remain fragile without telemetry and automated smoke suites. |

## Refactoring Plan

1. **Prompting toolkit alignment**
   - Extract the delimiter + JSON prompt rules from the proposal into a shared builder (e.g., `DocumentPromptBuilder`) that emits the `SYSTEM/META/...` contract and delimiter constants for both template generation and manual synthesis.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L36-L223】【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L359-L705】【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L21-L196】
   - Update `TemplateSuggestionService` to demand `---DOCUMENT_TYPE_JSON_START/END---` wrapped JSON, parse via the shared helper, and persist prompt metadata back onto `SemanticTypeProfile` for auditability.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L36-L223】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】
   - Refactor `InsightSynthesisService.GenerateManualSessionAsync` to compose requests through the builder (including `SYSTEM`, `META`, `OUTPUT REQUIREMENT` sections) and log the generated prompts for diagnostics, ensuring parity with the lean prompt architecture.【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L359-L705】【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L173-L196】

2. **Manual session & discovery integration**
   - Persist consolidated session artefacts (filled template, findings, telemetry) into projection-friendly models and surface them through diagnostics so dashboards can show manual synthesis freshness alongside automated insights.【F:samples/S13.DocMind/Services/ManualAnalysisService.cs†L90-L163】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】
   - Expose session timelines or recent activity via `DocumentDiscoveryProjection` (or a sibling projection) so manual runs appear in operator overviews and queue widgets without re-querying full collections.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L30-L144】【F:samples/S13.DocMind/wwwroot/app/views/analysis/list.html†L25-L159】

3. **Client contract realignment**
   - Regenerate Angular clients from OpenAPI, replace bespoke REST helpers (`ApiService.get('/Documents')`, `/analysis`) with typed SDK calls, and rename services/views to the new vocabulary (`SourceDocument`, `SemanticTypeProfile`, session timelines).【F:samples/S13.DocMind/wwwroot/app/services/documentService.js†L1-L143】【F:samples/S13.DocMind/wwwroot/app/services/analysisService.js†L1-L190】
   - Rewire dashboard/documents/templates screens to consume `SourceDocument.Summary`, `/timeline`, `/chunks`, and `/insights` data so the UI reflects ledger telemetry and manual synthesis outputs.【F:samples/S13.DocMind/Controllers/DocumentsController.cs†L52-L178】【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L52-L132】【F:samples/S13.DocMind/wwwroot/app/views/analysis/detail.html†L25-L112】

4. **Configuration & boot telemetry**
   - Introduce read/write APIs for `DocMindOptions`, persist edits via Koan configuration storage, and return registrar validation errors so the UI reflects real state.【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L103】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】
   - Update the Angular configuration module to load genuine snapshots, submit mutations, and display registrar/model health, closing the feedback loop for workshop demos.【F:samples/S13.DocMind/wwwroot/app/controllers/configurationController.js†L12-L152】【F:samples/S13.DocMind/wwwroot/app/services/configurationService.js†L56-L146】

5. **Discovery & diagnostics hardening**
   - Replace full scans in `DocumentDiscoveryProjectionBuilder` with change-aware refreshes, capture freshness windows, and feed drift metrics into diagnostics endpoints and dashboards.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L71-L198】
   - Extend diagnostics APIs to surface vector latency, manual session freshness, and retry pacing thresholds so operators spot staleness before it impacts users.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L71-L198】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L61-L123】

6. **Observability & CI guardrails**
   - Wire OpenTelemetry tracing/metrics around worker stages, AI calls, manual session prompts, and vector operations; document compose overrides and sampling defaults in README.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L774】
   - Automate HTTP-level tests covering upload → manual session execution → diagnostics, plus MCP smoke scripts, and run them in CI beside the existing harness tests.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】

## Delivery Order
1. Prompting toolkit alignment.
2. Manual session & discovery integration.
3. Client contract realignment.
4. Configuration & boot telemetry.
5. Discovery & diagnostics hardening.
6. Observability and CI guardrails.

Deferring a separate domain assembly keeps focus on prompt consistency, manual session visibility, and operational readiness that unlock the proposal’s primary outcomes.
