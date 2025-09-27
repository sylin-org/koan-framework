# S13.DocMind Next Steps (Proposal Delta)

## Discovery Projection Scalability
1. **Decouple refresh cadence from worker completion.** Introduce a scheduler or configurable throttle so the worker can signal for refresh without executing it synchronously after every aggregation, letting operations align rebuilds with off-peak windows.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L618-L647】
2. **Exercise the incremental builder at scale.** Use the testing blueprint to script load scenarios that confirm the change-detection path preserves overview/collection metrics and queue metadata for large datasets.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L110】

## Embedding Stage & Vector Observability
1. **Split out `GenerateEmbeddings`.** Refactor the worker so embedding generation emits its own stage telemetry and persists resumable snapshots before handing off to vision/insights stages.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L207-L325】【F:samples/S13.DocMind/Models/SourceDocument.cs†L186-L208】
2. **Bootstrap semantic profiles.** Extend `DocumentVectorBootstrapper` (or add a sibling task) to audit and backfill `SemanticTypeEmbedding` records, pausing suggestions when Weaviate is unavailable.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L126-L160】
3. **Publish vector health signals.** Capture adapter readiness/latency in the registrar and processing events so operators can see when the system degrades to cosine fallback.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L78】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L535-L648】

## Replay & Operational Quality
1. **Deliver replay/config endpoints.** Implement the `POST /api/processing/replay` and `GET /api/processing/config` endpoints (plus CLI/MCP shims) described in the proposal to complete the admin surface.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L21-L85】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L104】
2. **Wire health probes and telemetry.** Register the storage/vector/model health checks and OpenTelemetry wiring called out in the infrastructure plan so the boot report and `/health` reflect real readiness.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L110】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L78】
3. **Stand up DocMind test suites.** Create the unit/integration projects with fake providers and pipeline smoke tests, and hook them into CI as the testing blueprint prescribes.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L110】【d800db†L1-L12】

## Immediate Spikes
- **Incremental projection rehearsal:** Run the current builder against a captured dataset to measure refresh cost and identify delta queries to implement first.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L78】
- **Semantic profile audit:** Inventory existing profiles/embeddings to scope bootstrap coverage and fallback behavior before introducing the new stage.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L126-L160】
- **Replay tooling UX review:** Prototype the CLI/API surface for stage-targeted resumes before wiring into diagnostics to ensure operator workflows map to the documented endpoints.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L21-L85】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L104】
