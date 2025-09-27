# S13.DocMind Refactoring Plan (Updated)

## Context & Delta Synthesis
- **Entities vs. services mismatch** – The new `SourceDocument`, `DocumentChunk`, and `DocumentInsight` entities expose properties such as `ProcessingSummary`, `ChunkIds`, and vector adapters, yet `DocumentIntakeService` and the legacy `Services/DocumentAnalysisPipeline` still target removed members (`OriginalFileName`, `Summary`, `Suggestions`, `AssignedProfileId`) and legacy models (`File`, `Analysis`, `DocumentType`).
- **Competing infrastructure stacks** – `Program.cs` wires `AddDocMindProcessing` (legacy queue + hosted service) while `Infrastructure/DocMindRegistrar` attempts Koan auto-registration, leading to duplicate background workers (`DocumentProcessingHostedService` vs. `Infrastructure/DocumentAnalysisPipeline`) and inconsistent options (`DocumentPipelineQueueOptions` vs. `DocMindOptions`).
- **Queue contract drift** – `Infrastructure/DocumentPipelineQueue` (newer) expects `DocumentWorkItem` with `DocumentId` + stage metadata, while `Services/DocumentPipelineQueue` and its consumers still use string-based `FileId`, retry counters, and `ProcessingProfile`. Both versions coexist and block compilation.
- **Vector strategy inconsistency** – Vector adapter entities (`DocumentChunkEmbedding`, `SemanticTypeEmbedding`) live beside `[Vector]` fields on core entities and service code that continues saving embeddings through `Vector<T>.Save`, contradicting the Weaviate-based design.
- **Event persistence gap** – `DocumentProcessingEvent` now includes message/context metadata, but ingestion and pipeline code either log events only (`DocumentProcessingEventLogger`) or never sets correlation/timing fields required by diagnostics.
- **API & contracts drift** – Controllers and contracts assume timelines, chunk responses, and profile suggestions built atop the old pipeline; the new domain structure has no working projection layer to populate these responses.

## Layered Refactoring Strategy
The rebuild follows dependency layers—stabilize domain contracts, refit infrastructure, then reconstitute services, AI enrichment, and API surfaces. Each layer calls out explicit source modules to modify or remove.

### 1. Bedrock Entities & Persistence
1. **Converge entity schemas**
   - Normalize `SourceDocument`, `DocumentChunk`, `DocumentInsight`, and `DocumentProcessingEvent` to match the proposal (remove stray `[Vector]` arrays, finalize `ProcessingSummary`, ensure storage metadata lives under `StorageLocation`).
   - Reconcile enums and value objects so service code can reference a single set (`DocumentProcessingStage`, `DocumentProcessingStatus`, `DocumentProcessingSummary`).
2. **Finalize vector adapters**
   - Keep embeddings exclusively in `DocumentChunkEmbedding`/`SemanticTypeEmbedding`; migrate any required annotations into those records.
   - Provide helper methods for querying embeddings via Koan vector adapters instead of direct `[Vector]` usage.
3. **Persistence utilities**
   - Introduce repository/query helpers for the rebuilt entities (e.g., `SourceDocumentQueries`, `DocumentProcessingEventLog`) to centralize common lookups used by controllers and pipelines.

### 2. Infrastructure & Bootstrapping
1. **Program bootstrap alignment**
   - Replace the manual `AddDocMindProcessing` wiring in `Program.cs` with the Koan pattern: `.AddKoan().AddKoanMcp()` + module registrar discovery.
   - Remove `Services/DocMindRegistrar.cs` extension and rely solely on `Infrastructure/DocMindRegistrar` to register options, storage, queue, and hosted services.
2. **Options unification**
   - Bind `DocMindOptions` from configuration and retire `DocumentPipelineQueueOptions`/`DocumentAnalysisOptions` in favor of the consolidated processing settings.
3. **Queue consolidation**
   - Promote `Infrastructure/DocumentPipelineQueue` as the single implementation; rewrite `DocumentWorkItem` to track `Guid DocumentId`, requested stage, attempt counters, and correlation IDs per the blueprint.
   - Delete the legacy channel queue and hosted service once the new background service owns orchestration.
4. **File storage baseline**
   - Ensure `LocalDocumentStorage` (or alternative provider) persists files under the new storage metadata contract and surfaces dedupe hashes for intake.

### 3. Processing Pipeline & Background Services
1. **Rebuild pipeline orchestration**
   - Update `Infrastructure/DocumentAnalysisPipeline` to orchestrate stages: hydrate document, extract text/vision, persist chunks (`DocumentChunk`), generate insights (`DocumentInsight`), update `ProcessingSummary`, and enqueue follow-on tasks when needed.
   - Inject persistence-backed logging to create `DocumentProcessingEvent` entries for each significant step (with stage, status, message, context, duration, attempts).
2. **Queue-driven execution**
   - Implement a hosted worker that dequeues `DocumentWorkItem`s from the consolidated queue, handles concurrency via `DocMindOptions.Processing`, and requeues transient failures with backoff semantics.
3. **Retry & diagnostics**
   - Persist attempt counts, last error, and durations onto `SourceDocument.ProcessingSummary` and `DocumentProcessingEvent` to support timeline + operations dashboards.

### 4. AI, Extraction, and Intelligence Services
1. **Text & vision extraction**
   - Refactor `TextExtractionService` to use Koan `IAi` abstractions for OCR/vision when `ProcessingOptions.EnableVisionExtraction` is true; emit multi-channel chunks (text, vision frames).
   - Capture extraction metadata (tokens, frames, confidences) into chunk records and processing events.
2. **Insight synthesis pipeline**
   - Update `InsightSynthesisService`, `TemplateSuggestionService`, and related collaborators to operate on the new entities/embedding stores, producing structured payloads aligned with `DocumentInsight.StructuredPayload`.
   - Remove placeholder implementations (`VisionInsightService`, `TemplateGeneratorService`, etc.) that still target legacy models.
3. **Embedding workflow**
   - Route chunk/type embeddings through the vector adapter entities, provide fallbacks when the adapter is unavailable, and synchronize vector IDs with source entities.

### 5. API, Projections, and Experience Layer
1. **Controller refactor**
   - Update `DocumentsController`, `InsightsController`, and related endpoints to query the rebuilt entities/aggregations, returning responses expected by the Angular app and MCP tools.
   - Implement timeline endpoints backed by persisted `DocumentProcessingEvent`s instead of in-memory logs.
2. **Projection services**
   - Rebuild `DocumentInsightsService`, `DocumentAggregationService`, and diagnostics helpers to materialize summary DTOs (chunks, insights, suggestions, processing summaries) from the new data model.
3. **MCP tooling alignment**
   - Ensure MCP tool registrations and responses reference the updated contracts, especially for document upload, reprocessing, and insight retrieval workflows.

### 6. Observability, Ops, and Quality
1. **Diagnostics plumbing**
   - Implement `IDocumentProcessingEventSink` atop entity persistence, integrate with Koan tracing, and expose boot report metadata via the registrar.
   - Capture model usage, token counts, and error codes in events to support analytics.
2. **Testing strategy**
   - Establish unit/integration tests around intake validation, pipeline stage progression, embedding persistence, and controller projections using Koan’s in-memory providers where possible.
3. **Operational scripts & docs**
   - Provide setup scripts for MongoDB/Weaviate indices, sample configuration for AI providers, and troubleshooting guides reflecting the new architecture.

## Execution Roadmap
1. **Stabilize bedrock** – finalize entities, options, and vector adapters; remove conflicting legacy types.
2. **Rewire infrastructure** – adopt the Koan registrar bootstrap, consolidate the queue, and stand up the new hosted pipeline service.
3. **Rebuild pipeline services** – refactor intake, extraction, insight synthesis, and embedding flow on top of the new contracts.
4. **Restore projections & APIs** – update controllers and projection services to surface the enriched data and persisted events.
5. **Harden AI & ops** – wire production-ready AI calls, diagnostics, tests, and documentation to complete the rebuild.
