# S13.DocMind Refactoring Plan (Comprehensive Update)

## 1. Context and Delta Overview
- **Blueprint expectations** – The proposal outlines a Koan-first stack with auto-registered services, queue-driven pipelines, enriched entities (`SourceDocument`, `DocumentChunk`, `DocumentInsight`), persisted processing timelines, and Weaviate-backed vector adapters. It also prescribes real OCR/vision enrichment, structured insight synthesis, and MCP tooling parity. 
- **Current implementation reality** – The sample still mixes legacy pipeline artifacts with partial bedrock refactors. Core services continue to depend on removed members such as `SourceDocument.OriginalFileName`, `DocumentSummary`, and `[Vector]` arrays, while controllers and diagnostics rely on manual logging instead of persisted timelines. Bootstrap code wires services manually and omits the Koan auto-registrar flow entirely. 

## 2. Gap Deep-Dive
1. **Entity & contract drift**
   - `SourceDocument` retains `[Vector]` embeddings and summary fields that the active services no longer hydrate, yet `DocumentIntakeService` still expects `OriginalFileName`, `Summary`, and `Storage` members that do not exist, causing runtime/compile failures. 
   - `DocumentChunk` continues to host `[Vector]` properties, whereas the blueprint moves embeddings into dedicated adapter entities; downstream services still call `Vector<DocumentChunk>.Save`, bypassing the proposed adapter abstraction. 
2. **Pipeline orchestration mismatch**
   - `Infrastructure/DocumentAnalysisPipeline` stages documents but persists embeddings directly on chunks and documents, never writing `DocumentProcessingSummary.StageHistory` or capturing attempt telemetry, and it still reuses placeholder extraction + insight synthesis behaviors. 
   - Intake and pipeline components enqueue `DocumentWorkItem` instances with inconsistent signatures (string vs. `Guid` identifiers, legacy stage enums), preventing a cohesive queue-driven workflow. 
3. **Event persistence & diagnostics gap**
   - `DocumentProcessingEvent` now exposes `Message`, `Context`, and token metrics, yet orchestration code records only minimal fields and the diagnostics service remains log-based, leaving timeline endpoints without durable data. 
4. **Bootstrap & configuration debt**
   - `Program.cs` still chains `.AsWebApi().AsProxiedApi()` and manually registers services, ignoring the Koan auto-registrar and the consolidated `DocMindOptions` bindings introduced in the blueprint. 
5. **AI & discovery shortfalls**
   - `TextExtractionService`, `InsightSynthesisService`, and `TemplateSuggestionService` continue to rely on stubbed logic, lack OCR/vision integration, and bypass the semantic typing/Weaviate flows described in the proposal. 
   - `DocumentAggregationService` and `DocumentInsightsService` still target legacy projection models, so controllers cannot produce the multi-modal insight payloads promised by the design documents. 

## 3. Layered Refactoring Blueprint
### Layer A – Bedrock Entities & Persistence
1. **Schema realignment**
   - Prune inline `[Vector]` fields from `SourceDocument`/`DocumentChunk`, move embeddings into `DocumentChunkEmbedding` and `SemanticTypeEmbedding`, and ensure `ProcessingSummary`/`StorageLocation` match the blueprint schema.
   - Restore missing members required by services (e.g., `OriginalFileName`, storage metadata) or adapt services to the new contract; prefer aligning both directions to the blueprint definitions.
2. **Consistency helpers**
   - Introduce repository/query helpers for `SourceDocument`, `DocumentProcessingEvent`, and `DocumentInsight` to centralize lookups, timeline retrieval, and stage transitions.
3. **Options & configuration**
   - Finalize `DocMindOptions` binding under the `DocMind` section and remove legacy per-service option classes.

### Layer B – Infrastructure & Bootstrapping
1. **Koan-native bootstrap**
   - Replace manual service registration in `Program.cs` with `AddKoan(...).AddKoanMcp()`, ensure `DocMindRegistrar` implements the module auto-registrar interface, and migrate residual singletons into the registrar.
2. **Queue consolidation**
   - Collapse duplicate queue implementations into a single `DocumentPipelineQueue` honoring the blueprint’s async stream dequeue, retry strategy, and `Guid` document identifiers.
3. **Storage baseline**
   - Update `LocalDocumentStorage` (or replacement adapter) to write metadata compatible with the updated `StorageLocation` contract and expose dedupe hashes for intake.

### Layer C – Processing Pipeline & Background Workers
1. **Stage orchestration**
   - Rewrite `DocumentAnalysisPipeline` to honor the blueprint stage map (ExtractText → ExtractVision → Chunk → Analyze → Insights → Complete) and persist `ProcessingSummary.StageHistory`, attempt counts, and timestamps at each transition.
2. **Work execution**
   - Implement a hosted worker that dequeues work items, enforces concurrency from `DocMindOptions`, and requeues transient failures with exponential backoff and capped retries.
3. **Event sink**
   - Persist every stage change via `DocumentProcessingEvent` with populated `Message`, `Context`, metrics, and correlation IDs so the timeline endpoint becomes durable.

### Layer D – AI, Enrichment, and Discovery Services
1. **Extraction overhaul**
   - Integrate Koan `IAi.VisionPrompt`/OCR pathways for images and PDF pages, capturing confidence scores and frame metadata inside `DocumentVisionProcessingSummary` and chunk records.
2. **Insight synthesis**
   - Replace placeholder summarization with prompt-driven pipelines that fill `DocumentInsight.StructuredPayload`, map insights to chunks, and persist embeddings through vector adapters.
3. **Suggestion & discovery**
   - Rebuild `TemplateSuggestionService` and `DocumentAggregationService` to query embeddings via the Koan vector adapter, generate semantic type suggestions, and hydrate API DTOs from the normalized entities.

### Layer E – API, MCP, and Experience
1. **Controller alignment**
   - Update API controllers to query the rebuilt entities, surface timeline data from persisted events, and deliver the chunk/insight payloads described in the blueprint contracts.
2. **MCP tooling**
   - Regenerate MCP tools to operate on the new workflows (upload, reprocess, retrieve insights), ensuring they call the refactored endpoints and surface diagnostic metadata.
3. **Testing & docs**
   - Create integration tests using Koan in-memory providers for intake, pipeline progression, and insight retrieval; refresh README/MCP docs to reflect the rebuilt flows.

## 4. Sequenced Execution Roadmap
1. **Stabilize bedrock** – Align entity schemas, storage contracts, and configuration; remove conflicting legacy models and regenerate migrations if applicable.
2. **Adopt Koan bootstrap** – Introduce the auto-registrar, consolidate queue infrastructure, and wire the hosted worker with resilient retry logic.
3. **Rebuild processing pipeline** – Implement the stage orchestration, event persistence, and telemetry updates while integrating real extraction and insight services.
4. **Reconstruct discovery layer** – Update controllers, aggregation services, and MCP tooling to surface the new data model end-to-end.
5. **Harden AI & operations** – Finalize model catalog integration, add observability hooks, and expand automated test coverage.

## 5. Immediate Next Steps (Iteration Focus)
1. **Entity/schema cleanup sprint**
   - Remove legacy `[Vector]` properties from `SourceDocument` and `DocumentChunk`, finalize storage/summary contracts, and update affected services/tests to compile against the corrected models.
   - Introduce `DocumentChunkEmbedding`/`SemanticTypeEmbedding` persistence helpers and migrate any existing save calls to the adapter-based API.
2. **Bootstrap & queue consolidation**
   - Refactor `Program.cs` to use the Koan auto-registrar pattern and move all DocMind-specific registrations into `DocMindRegistrar`.
   - Delete the legacy services queue implementation, update intake/pipeline code to use the consolidated `DocumentWorkItem` contract, and add telemetry for enqueue/dequeue transitions.
3. **Timeline persistence enablement**
   - Implement a repository-backed `IDocumentProcessingEventSink`, update intake/pipeline stages to emit full events (message, context, metrics), and retrofit diagnostics/timeline endpoints to read from the persistent store.
4. **Planning for AI enrichment**
   - Draft technical spikes for OCR/vision integration (model selection, prompt templates, cost considerations) and structured insight synthesis, feeding into subsequent implementation iterations.
