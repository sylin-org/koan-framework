# S13.DocMind Next Steps (Proposal Delta)

## Discovery Refresh Hardening
1. **Decouple rebuild execution.** Introduce a scheduler or projection refresh queue so the worker signals completion without performing `DocumentDiscoveryProjectionBuilder.RefreshAsync` inline, keeping throughput stable under load.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L618-L648】
2. **Validate incremental accuracy.** Build load/regression fixtures that hammer the change-detection path and confirm overview, collection, and queue metrics remain correct as the corpus grows.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L20-L157】【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L67】

## Embedding Stage & Observability
1. **Promote `GenerateEmbedding`.** Split embeddings out of extraction so the worker records dedicated stage events, metrics, and resumable snapshots before downstream insight generation.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L207-L325】【F:docs/chunks/S13-DocMind/02_entity_models.md†L163-L175】
2. **Capture adapter telemetry.** Emit latency, token, and readiness metrics for embedding and suggestion calls via processing events and diagnostics to satisfy the observability guidance.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L207-L325】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L126-L160】【F:docs/chunks/S13-DocMind/03_ai_processing.md†L55-L75】

## Vector Bootstrap & Health Signals
1. **Audit semantic profile vectors.** Extend `DocumentVectorBootstrapper` (or a sibling task) to verify `SemanticTypeEmbedding` records, backfill missing vectors, and block suggestions when adapters are unavailable.【F:samples/S13.DocMind/Infrastructure/DocumentVectorBootstrapper.cs†L20-L37】【F:docs/chunks/S13-DocMind/02_entity_models.md†L140-L175】
2. **Publish readiness indicators.** Surface vector availability and fallback state through the registrar, boot report, and diagnostics responses so operators can react to degraded modes.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L74】【F:samples/S13.DocMind/Services/TemplateSuggestionService.cs†L126-L160】【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】

## Replay & Operational Guardrails
1. **Finish admin endpoints.** Implement `POST /api/processing/replay` and `GET /api/processing/config` (plus CLI/MCP shims) to unlock the documented stage-targeted recovery flows.【F:samples/S13.DocMind/Controllers/ProcessingController.cs†L21-L75】【F:docs/chunks/S13-DocMind/04_api_ui_design.md†L6-L101】
2. **Register health checks & telemetry.** Add storage, embedding, and model probes plus OpenTelemetry wiring to `DocMindRegistrar` and expose them through the boot report and `/health` endpoints.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L74】【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】
3. **Stand up DocMind tests.** Create the unit/integration projects with fake providers and pipeline smoke tests outlined in the testing plan, wiring them into CI so regressions catch proposal deltas automatically.【F:docs/chunks/S13-DocMind/07_testing_ops.md†L1-L67】

## Immediate Spikes
- **Projection refresh rehearsal:** Measure refresh duration and write amplification against a captured dataset to size the scheduler/throttle work.【F:samples/S13.DocMind/Services/DocumentDiscoveryProjectionBuilder.cs†L22-L110】
- **Embedding stage design:** Prototype stage promotion on a branch to confirm retry semantics, telemetry shape, and token accounting requirements before cutting over.【F:samples/S13.DocMind/Infrastructure/DocumentProcessingWorker.cs†L207-L325】
- **Vector health instrumentation:** Draft registrar/diagnostics payloads that surface adapter readiness so UI and ops flows can consume them once implemented.【F:samples/S13.DocMind/Infrastructure/DocMindRegistrar.cs†L30-L74】【F:docs/chunks/S13-DocMind/05_infrastructure.md†L90-L112】
