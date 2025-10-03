# S13.DocMind Next Steps (Updated)

## Prompting Toolkit Alignment
1. **Stand up a shared prompt builder.** Implement a reusable service that emits the proposal’s delimiter constants and `SYSTEM/META/...` scaffolding for template generation and manual synthesis flows.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L36-L223】【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L21-L196】
2. **Harden template generation parsing.** Require `---DOCUMENT_TYPE_JSON_START/END---` wrapped JSON, extract via the shared builder, and persist the resulting prompt metadata on `SemanticTypeProfile` for traceability.【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L36-L223】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】
3. **Refactor manual synthesis inputs.** Pipe `GenerateManualSessionAsync` through the builder, log composed prompts, and surface delimiter parsing diagnostics so sessions follow the lean prompt architecture.【F:samples/S13.DocMind/Services/InsightSynthesisService.cs†L359-L705】【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L173-L196】

## Manual Sessions & Discovery
1. **Project manual outputs.** Persist filled templates, findings, and telemetry as projection-friendly snapshots so discovery/diagnostics endpoints can report manual freshness alongside automated insights.【F:samples/S13.DocMind/Services/ManualAnalysisService.cs†L90-L163】【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】
2. **Expose session activity.** Extend `DocumentDiscoveryProjection` (or add a sibling view) to include recent manual runs and expose them on dashboard widgets and diagnostics feeds.【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L30-L144】【F:samples/S13.DocMind/wwwroot/app/views/analysis/list.html†L25-L159】
3. **Surface manual artefacts in UI.** Update analysis and dashboard screens to display manual synthesis freshness, recent runs, and linked documents without re-querying entire collections.【F:samples/S13.DocMind/wwwroot/app/views/analysis/detail.html†L25-L112】【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L52-L132】

## Client Contract Realignment
1. **Regenerate SDKs and rename services.** Use `dotnet koan client` to produce Angular SDKs, swap bespoke `ApiService` calls for typed clients, and align naming with `SourceDocument`/`SemanticTypeProfile` vocabulary.【F:samples/S13.DocMind/wwwroot/app/services/documentService.js†L1-L143】【F:samples/S13.DocMind/wwwroot/app/services/analysisService.js†L1-L190】
2. **Refresh dashboard & document views.** Bind cards and tables to `SourceDocument.Summary`, `/timeline`, `/chunks`, and diagnostics responses so progress indicators reflect real ledger state.【F:samples/S13.DocMind/Controllers/DocumentsController.cs†L52-L178】【F:samples/S13.DocMind/wwwroot/app/views/dashboard/index.html†L52-L132】
3. **Upgrade template gallery.** Consume the enriched `/api/document-types` DTOs, show stored prompts/schemas, and wire prompt-test modals that capture revisions for audits.【F:samples/S13.DocMind/Controllers/TemplatesController.cs†L13-L47】【F:samples/S13.DocMind/Models/SemanticTypeProfile.cs†L1-L82】

## Configuration & Telemetry
1. **Persist `DocMindOptions`.** Add read/write endpoints, validate with `DocMindOptionsValidator`, and store changes via Koan configuration storage so runtime state matches the UI.【F:samples/S13.DocMind/Infrastructure/DocMindOptions.cs†L6-L103】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】
2. **Wire Angular configuration to reality.** Load real option snapshots, surface registrar/model health, and persist edits, closing the feedback loop for operators.【F:samples/S13.DocMind/wwwroot/app/controllers/configurationController.js†L12-L152】【F:samples/S13.DocMind/Controllers/ModelsController.cs†L27-L126】
3. **Document telemetry setup.** Capture OpenTelemetry exporter guidance in README/compose overrides and highlight sampling defaults before instrumenting code paths.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L774】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】

## Discovery & Diagnostics Hardening
1. **Implement incremental refresh.** Replace full scans with change-window aggregation, persist freshness timestamps, and expose drift metrics on discovery responses.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L71-L198】
2. **Augment diagnostics.** Add vector latency, manual session freshness, and retry pacing signals to diagnostics endpoints and mirror them in dashboard queue widgets.【F:samples/S13.DocMind/Services/ProcessingDiagnosticsService.cs†L71-L198】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L61-L123】
3. **Automate end-to-end tests.** Add HTTP-level integration suites covering upload → manual session execution → diagnostics and run them in CI beside the harness tests.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】

## Observability & CI
1. **Instrument OpenTelemetry.** Add tracing/metrics around worker stages, AI calls, and manual prompts, then document defaults so compose deployments expose the data.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L17-L774】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L123】
2. **Extend CI coverage.** Layer HTTP + MCP smoke tests on top of the harness project and gate PRs on the combined suite.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】

## Immediate Spikes
- **Prompt builder playground:** Prototype the shared builder against the documented delimiter patterns before wiring it into production services.【F:docs/chunks/S13-DocMind/AI_Prompting_Patterns_from_GDoc.md†L21-L196】
- **Discovery delta prototype:** Experiment with change-token based refresh or incremental aggregation on a small dataset to confirm projection performance gains.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L52-L199】
- **Telemetry/CI scaffold:** Extend the existing harness into an HTTP smoke test and validate OpenTelemetry exporter wiring ahead of full instrumentation.【F:tests/S13.DocMind.IntegrationTests/DocMindProcessingHarnessTests.cs†L15-L71】
