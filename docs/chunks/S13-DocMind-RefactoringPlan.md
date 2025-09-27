# S13.DocMind Refactoring Plan (Proposal Delta)

## Remaining Gaps
- **Discovery projection cadence is still worker-bound and unproven under load.** Change detection now prevents unnecessary rescans and the API surfaces freshness metadata, but `DocumentProcessingWorker` still invokes the refresh synchronously after each completion and there are no load tests ensuring the incremental path holds up at scale.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L24-L157】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L618-L647】【F:samples/S13.DocMind/Services/DocumentInsightsService.cs†L9-L91】
- **Embedding stage instrumentation is still folded into extraction.** The worker generates chunk embeddings but never transitions through the dedicated `GenerateEmbeddings` stage, leaving retry telemetry and timelines unable to isolate vector failures the proposal expects to monitor separately.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L207-L325】【F:samples/S13.DocMind/Models/SourceDocument.cs†L186-L208】【F:docs/chunks/S13-DocMind/03_ai_processing.md†L31-L70】
- **Vector readiness stops at ensuring the chunk index.** The bootstrapper only provisions the chunk collection and the suggestion path does not gate on semantic profile embeddings or surface adapter health, so Weaviate outages remain invisible beyond log noise.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L126-L160】
- **Replay tooling, health checks, and tests remain missing.** `ProcessingController` still exposes only queue, timeline, and retry, the registrar only reports configuration with no health probes, and the DocMind test suites outlined in the proposal have not been created.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L21-L85】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L78】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L104】【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L110】【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L110】【d800db†L1-L12】

## Refactoring Streams

### 1. Discovery Projection Scalability
1. **Decouple refresh orchestration.** Move projection rebuilds onto a scheduled background job (or configurable throttle) so the worker can signal a refresh without performing it inline, preserving throughput while keeping queue metadata fresh.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L618-L647】
2. **Prove incremental accuracy.** Add targeted load tests or fixtures that hammer the change-detection path and assert overview, collection, and queue outputs remain correct as the corpus grows.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L24-L157】【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L110】

### 2. Embedding Stage & Vector Observability
1. **Promote embeddings to a first-class stage.** Split the worker so chunk embedding generation records `GenerateEmbeddings` events, updates stage telemetry, and persists snapshots that retries can skip when vectors already exist.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L207-L325】【F:samples/S13.DocMind/Models/SourceDocument.cs†L186-L208】
2. **Bootstrap semantic profiles.** Extend the vector bootstrapper (or add a companion job) to verify `SemanticTypeEmbedding` entries, backfill missing vectors, and pause suggestions when adapters are unavailable instead of silently falling back.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L126-L160】
3. **Surface adapter health.** Emit readiness, latency, and fallback mode metrics through the registrar, processing events, and diagnostics queue so operators can detect degraded vector service before confidence drops.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L78】【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L535-L648】

### 3. Replay Tooling, Health, and Quality Gates
1. **Deliver the documented admin surface.** Add the `POST /api/processing/replay` and `GET /api/processing/config` endpoints plus CLI/MCP bindings so operators can drive stage-targeted resumes the proposal calls for.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L21-L85】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L104】
2. **Register health checks and telemetry.** Wire storage/vector/model health probes, OpenTelemetry exporters, and boot report readiness indicators so operational dashboards reflect real availability.【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L110】【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L23-L78】
3. **Stand up DocMind test suites.** Create the unit/integration projects described in the testing plan with fake AI/vector providers and pipeline smoke tests, and add them to CI.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L110】【d800db†L1-L12】

## Delivery Order
1. **Iteration 2 wrap-up – Discovery projection scalability.** Deliver incremental refresh logic plus freshness metadata so analytics endpoints scale without full rescans.
2. **Iteration 3 – Embedding & vector observability.** Promote embeddings to their own stage, finish semantic profile bootstrap, and surface adapter health.
3. **Iteration 4 – Operations & quality.** Ship the replay/config endpoints, register health probes, and land DocMind-specific automated tests.

Each iteration builds on the durable ledger delivered earlier while closing the remaining discovery, vector, and operational deltas.
