# S13.DocMind Next Steps (Updated)

## Manual Analysis & Multi-Document Synthesis
1. **Introduce session entities and APIs.** Model `DocumentAnalysisSession` with selected document IDs, prompt revisions, and stored outputs, then expose CRUD + run endpoints alongside the existing document controllers.【F:samples/S13.DocMind/Controllers/AnalysisController.cs†L13-L39】【F:docs/chunks/S13-DocMind/Implementation_Examples_GDoc_Integration.md†L520-L599】
2. **Extend synthesis for multi-document prompts.** Upgrade `InsightSynthesisService` (or a dedicated coordinator) to accept multi-document requests, reuse the delimiter templates, and persist structured outputs that power manual session summaries.【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L29-L170】【F:docs/chunks/S13-DocMind/Implementation_Examples_GDoc_Integration.md†L520-L599】
3. **Rebuild the analysis UI.** Replace the legacy insight grid with a session-centric experience: document picker, prompt editor, run/poll actions, and revision history, backed by regenerated TypeScript clients.【F:samples/S13.DocMind/wwwroot/app/controllers/analysisController.js†L1-L158】【F:samples/S13.DocMind/wwwroot/app/views/analysis/list.html†L13-L124】

## Client Contract Realignment
1. **Regenerate SDKs and rename services.** Use `dotnet koan client` to generate Angular clients, rename services/models to the new vocabulary, and update dashboard/document/templates controllers to consume `SourceDocument` summaries and timelines.【F:samples/S13.DocMind/wwwroot/app/services/documentService.js†L24-L130】【F:samples/S13.DocMind/Controllers/DocumentsController.cs†L52-L170】
2. **Refresh dashboard components.** Swap legacy fields (`documentTypeName`, `doc.name`, `file.state`) for the new DTO properties, and surface queue/timeline metrics so cards and tables reflect live processing status.【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L13-L125】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L30-L115】
3. **Update template gallery.** Point the Angular template views at `/api/document-types` DTOs that include prompts/schemas, expose prompt-test modals, and capture revisions for future audits.【F:samples/S13.DocMind/Controllers/TemplatesController.cs†L13-L47】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】

## Configuration & Telemetry
1. **Persist `DocMindOptions`.** Add REST endpoints that read/write the options object, validate via `DocMindOptionsValidator`, and persist edits (e.g., Mongo or appsettings override).【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L84】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L122】
2. **Wire Angular configuration to reality.** Load real option snapshots, surface registrar notes/health, allow edits, and refresh model installation statuses using the existing catalog endpoints.【F:samples/S13.DocMind/wwwroot/app/controllers/configurationController.js†L12-L152】【F:samples/S13.DocMind/Controllers/ModelsController.cs†L27-L126】
3. **Publish telemetry guidance.** Document OpenTelemetry exporter setup in README and compose overrides, then land tracing/metrics hooks around worker stages and AI calls.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L36-L200】

## Discovery & Diagnostics Hardening
1. **Incremental projection refresh.** Replace full corpus scans with change-window aggregation and persist freshness timestamps/latency metrics that feed both dashboard and diagnostics APIs.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L181】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L71-L165】
2. **Drift alerts & backlog views.** Extend diagnostics responses with projection freshness, vector readiness, and retry drift thresholds; mirror the data in the dashboard’s queue widgets.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L102-L165】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L60-L118】
3. **Automate end-to-end tests.** Add HTTP-level integration tests for upload→session execution, template generation, and diagnostics, then run them in CI alongside harness tests.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】

## Immediate Spikes
- **Manual session domain spike:** Prototype a minimal `DocumentAnalysisSession` persistence model and controller to validate how session states map onto existing `DocumentProcessingEvent` timelines.【F:samples/S13.DocMind/Models/DocumentProcessingEvent.cs†L13-L70】
- **Prompt builder playground:** Capture transcripts using the delimiter patterns before wiring them into `InsightSynthesisService` so manual sessions launch with validated prompts.【F:docs/chunks/S13-DocMind/Implementation_Examples_GDoc_Integration.md†L520-L599】
- **Telemetry/CI scaffold:** Experiment with Koan’s OpenTelemetry hooks and extend the existing harness project into an HTTP smoke test to prove out the CI additions before wiring the full suites.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】
