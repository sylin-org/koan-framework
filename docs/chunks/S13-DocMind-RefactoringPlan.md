# S13.DocMind Refactoring Plan (Delta Alignment, Bedrock-First)

## 1. Blueprint Commitments vs. Reality Check
- **Proposal baseline** – The S13.DocMind blueprint calls for Koan-native auto-registration, Mongo-backed entities (`SourceDocument`, `DocumentChunk`, `DocumentInsight`, `DocumentProcessingEvent`, `SemanticTypeProfile`), optional Weaviate vector adapters, a simple background worker orchestrator, and AI services that deliver real OCR/vision extraction, structured insights, and semantic template suggestions.
- **Current implementation snapshot** – The sample now includes refactored entities and a channel-backed `DocumentPipelineQueue`, yet core services still dereference members that no longer exist (`SourceDocument.Storage`, `DocumentChunk.Summary`, `ExtractedChunk.Content`), the hosted pipeline only performs text extraction, semantic profiles still embed `[Vector]` arrays, and bootstrap wiring does not register the registrar or background workers. Diagnostics and controllers query persisted events but the sink omits `Detail`/`Context` data that contracts expect.

This plan documents the concrete delta and prescribes a bedrock-first rebuild path that honors the proposal while fixing the implementation debt uncovered in `samples/S13.DocMind`.

## 2. Layered Delta Analysis & Refactoring Moves

### Layer A – Domain Foundations (Entities, Value Objects, Options)
**Observed gaps**
- `SourceDocument` exposes storage keys but services (e.g., `TextExtractionService`) still expect a `Storage` aggregate with `.Path`, leading to compile/runtime failures.
- `SemanticTypeProfile` still carries an inline `[Vector]` property instead of delegating to `SemanticTypeEmbedding`, contradicting the proposal and blocking Weaviate abstraction.
- `DocumentProcessingSummary` lacks flags for downstream services (`ContainsImages`, `LastStage`) and is not updated consistently by the pipeline.
- Options binding (`DocMindOptions`) exists, but no validation guards ensure queue/AI settings are sane, and configuration defaults disagree with docs (e.g., chunk sizing vs. proposal token counts).

**Refactoring actions**
1. **Restore coherent storage contract** – Introduce a `DocumentStorageLocation` value object (bucket, key, provider path) on `SourceDocument`; update intake/storage services to populate it and adjust `TextExtractionService` to resolve the file path through `IDocumentStorage` rather than the removed `Storage` property.
2. **Vector segregation cleanup** – Remove `[Vector]` from `SemanticTypeProfile`, add persistence helpers for `SemanticTypeEmbedding`, and adapt template suggestion services to use the adapter-first API.
3. **Summary alignment** – Extend `DocumentProcessingSummary` with the status markers promised in the docs (vision extracted, contains images, last completed stage) and ensure pipeline updates them atomically.
4. **Options hardening** – Apply `ValidateDataAnnotations`/custom validators to `DocMindOptions`, enforce minimum/maximums for queue capacity, retry policy, and chunk sizes, and sync defaults with the proposal tables.

### Layer B – Infrastructure & Bootstrapping
**Observed gaps**
- `Program.cs` only adds controllers; it never invokes `KoanModule.LoadAll()` or otherwise discovers `DocMindRegistrar`, leaving hosted services and options unregistered.
- The registrar wires everything manually but lacks configuration binding for AI/vector providers, and no health checks or boot diagnostics are exposed as promised in the blueprint.
- Docker/environment scaffolding in `appsettings*.json` retains legacy keys (e.g., `Processing:MaxRetryAttempts` vs. proposal's `MaxRetries`).

**Refactoring actions**
1. **Bootstrap the module** – Ensure `Program.cs` calls `builder.Services.AddKoan(...).AddKoanMcp().AddKoanModules(typeof(DocMindRegistrar).Assembly);` (or the equivalent auto-registrar hook) so DocMind services and hosted workers activate.
2. **Registrar polish** – Move configuration binding (`AddOptions<DocMindOptions>().BindConfiguration("DocMind")`) and provider capability detection into `DocMindRegistrar`, and add boot report entries for storage, AI, and vector readiness.
3. **Configuration convergence** – Rename appsettings keys to the `DocMind:*` hierarchy, supply `.env.sample` guidance, and align Docker compose env vars with the blueprint naming scheme.
4. **Health/diagnostics endpoints** – Register the storage/vector/model health checks described in the infrastructure doc and surface them through Koan's health middleware.

### Layer C – Data Access & Storage Services
**Observed gaps**
- `LocalDocumentStorage.SaveAsync` writes files but never returns the structured metadata the rest of the system expects (hash, version id, provider path), resulting in inconsistent dedupe behavior.
- Repository-style helpers for querying documents/events/insights are absent; services call `Entity<T>.All()` repeatedly, leading to inefficient full-collection scans.
- No migration/bootstrap tasks populate `SemanticTypeProfile` or ensure vector indices beyond chunk embeddings.

**Refactoring actions**
1. **Normalize storage responses** – Introduce a `StoredDocumentDescriptor` aligning with the proposal (hash, provider code, physical path) and refactor intake/pipeline callers accordingly.
2. **Repository helpers** – Add extension helpers (`SourceDocumentRepository`, `ProcessingEventRepository`) that expose filtered queries (e.g., pending documents, timeline fetch) to eliminate `All()` scans and concentrate data access rules.
3. **Bootstrap data** – Extend `DocumentVectorBootstrapper` (or a new bootstrap task) to seed semantic profiles, ensure both chunk and semantic embedding indices exist, and optionally populate sample templates for demos/tests.

### Layer D – Processing Pipeline & Background Workers
**Observed gaps**
- The hosted `DocumentAnalysisPipeline` only executes text extraction, skips vision, insight synthesis, and template suggestion, and does not honor the simple polling worker pattern described in the proposal.
- Retry handling uses `DocumentPipelineQueue` with channel/jitter complexity that the blueprint deliberately avoided; moreover, `HandleFailureAsync` attempts to reschedule even when retries are exhausted without updating `DocumentProcessingSummary` or `ProcessingEvent.Detail`.
- Stage events persist `Detail`, but contextual metadata is sparse (no metrics for chunk/page counts on upload, missing `ContainsImages` indicator).
- Intake requeue logic always enqueues `ExtractText` regardless of desired stage, ignoring the stage parameter on `AssignProfileAsync` / `RequeueAsync` flows.

**Refactoring actions**
1. **Adopt the proposal’s worker model** – Replace the channel-based queue with the simpler `DocumentProcessingWorker` polling pattern (or encapsulate the queue behind a worker facade) so the implementation matches the design docs and reduces concurrency debt.
2. **Complete stage orchestration** – Implement the full stage pipeline (vision analysis, insight synthesis, embedding generation, template suggestion) with explicit updates to `SourceDocument.Status`, `Summary`, and `DocumentProcessingEvent` records per stage.
3. **Retry semantics** – Rework failure handling to mark documents failed when retries are exhausted, emit terminal events with `IsTerminal = true`, and respect the requested retry stage when requeuing.
4. **Context-rich telemetry** – Populate event context/metrics (e.g., chunk counts, word counts, token usage) and align event detail strings with the OpenAPI contracts.

### Layer E – AI, Insights, and Discovery Services
**Observed gaps**
- `InsightSynthesisService` still references `DocumentChunk.Summary` and `ExtractedChunk.Content`, properties that no longer exist, so the project will not compile.
- `TextExtractionService` returns placeholder image text and no OCR metadata, conflicting with the “real AI extraction” objective; there is no vision service integration despite `VisionInsightService` existing.
- `TemplateSuggestionService` and `DocumentAggregationService` rely on legacy heuristics and do not touch vector adapters or structured payloads.
- Controllers return simplified DTOs that omit structured payloads, embeddings, or timeline correlations described in the proposal.

**Refactoring actions**
1. **Fix compilation drift** – Update AI services to use the new entity/value object members (`ExtractedChunk.Content` → `ExtractedChunk.Text`, `DocumentChunk.Text`, etc.) and add comprehensive unit coverage to prevent regressions.
2. **Implement true OCR/Vision flows** – Wire `VisionInsightService` to `IAi.VisionPrompt` with configurable models, persist extracted captions/entities into `DocumentInsight`, and record confidence metrics.
3. **Structured insight synthesis** – Replace the fallback summary logic with prompt templates that output structured payloads mapped into `DocumentInsight.Metadata/StructuredPayload`; ensure embeddings are generated and stored via adapters.
4. **Discovery rebuild** – Refactor aggregation/suggestion services and controllers to project `DocumentInsight` data into the response contracts (collections, analytics dashboards) promised in the docs, leveraging vector search when available.

### Layer F – Experience, MCP, and Operational Readiness
**Observed gaps**
- MCP endpoints/tools have not been regenerated; current controllers expose limited functionality and lack timeline/insight projections documented in `04_api_ui_design.md`.
- No automated tests guard intake dedupe, pipeline progression, or diagnostics. Observability hooks (OpenTelemetry, boot report entries) mentioned in the blueprint are missing.
- Documentation (README, next steps) still references removed extension methods (`AddDocMindProcessing`) and outdated queue semantics.

**Refactoring actions**
1. **API/MCP alignment** – Update controllers and regenerate MCP manifests/clients to expose the refactored workflow (upload, timeline, insight retrieval, retry) with the enhanced DTOs.
2. **Testing & observability** – Introduce integration tests using Koan in-memory adapters, add OpenTelemetry spans around AI calls, and publish health/boot diagnostics as part of CI.
3. **Docs & tooling refresh** – Rewrite README/setup guides to describe the new bootstrap, environment variables, and AI/vector prerequisites; ship reset scripts and sample fixtures per the proposal.

## 3. Execution Roadmap (Break & Rebuild Friendly)
1. **Stabilize bedrock (Layer A + B)** – Fix entity/storage contracts, remove inline vectors, enforce options validation, and ensure bootstrap/registrar wiring is functional. Expect to break/repair services as needed.
2. **Reconstitute data services (Layer C)** – Normalize storage descriptors, add repositories, and seed baseline semantic profiles/vector indices.
3. **Rebuild processing workflow (Layer D)** – Swap in the proposal-aligned worker, implement full stage orchestration, and harden retry/event semantics.
4. **Deliver AI & discovery features (Layer E)** – Implement OCR/vision, structured insights, vector-powered suggestions, and update controllers.
5. **Finalize experience & ops (Layer F)** – Regenerate MCP tooling, add automated tests + observability, and update documentation.

Each milestone should leave the sample runnable (even if feature-limited) before layering additional capabilities, preserving a clear path to the proposal’s end state.
