## **AI Processing & Pipeline Refactoring Plan**

### 1. Guiding Principles

- **Lightweight orchestration**: Retain the hosted worker + channel queue architecture introduced previously; avoid Flow dependencies while still modelling explicit stages and retries.
- **Composable services**: Split the current monolithic analysis service into focused collaborators that map 1:1 with the new domain models.
- **Provider-agnostic**: Rely on Koan AI abstractions (`AI.Prompt`, `AI.VisionPrompt`, `AI.Embed`) and storage helpers so switching between Ollama/OpenAI or different OCR providers is configuration-only.
- **Observable by default**: Emit `DocumentProcessingEvent` entries for every stage transition and surface them via API + MCP tools for debugging and UX timelines.

### 2. Proposed Processing Components

| Component | Responsibility | Key Dependencies | Notes |
|-----------|----------------|------------------|-------|
| `DocumentIntakeService` | Streams uploads, hashes content, enqueues work items | Koan storage, `Channel<DocumentWorkItem>` | Returns `DocumentUploadReceipt` for UI immediate feedback. |
| `DocumentAnalysisPipeline` (Hosted Service) | Background worker that drives staged processing | `System.Threading.Channels`, `ILogger`, Koan AI | Handles batching, retries, and concurrency throttle. |
| `TextExtractionService` | Extracts textual content from PDFs, DOCX, text, images | PdfPig, OpenXML, OCR provider | Writes `DocumentChunk` records and updates `Summary`. |
| `VisionInsightService` | Runs `AI.VisionPrompt` for diagrams/screenshots | Koan AI Vision, optional caching | Produces `DocumentInsight` entries flagged as `Vision`. |
| `InsightSynthesisService` | Generates structured facts from text chunks | Koan AI text models | Operates per chunk, merges into summary. |
| `TemplateSuggestionService` | Suggests `SemanticTypeProfile` matches | Koan embeddings + Weaviate adapter | Falls back to lexical heuristics when embeddings disabled. |
| `InsightAggregationService` | Builds multi-document `InsightCollection` | Koan AI prompt templates | Supports workspace-level intelligence scenarios. |

### 3. Pipeline Flow

1. **Upload** – `DocumentsController.Upload` validates file size/type, streams to storage, creates `SourceDocument`, and posts a `DocumentWorkItem` containing the document ID and optional assigned profile.
2. **Queue Intake** – `DocumentIntakeService` writes a `DocumentProcessingEvent` (`Stage = Upload`, `Status = Queued`) and places work on `Channel<DocumentWorkItem>`.
3. **Extraction Stage**
   - Pipeline fetches work; `TextExtractionService` resolves the storage object, extracts text, and creates ordered `DocumentChunk` entities.
   - Updates `SourceDocument.Summary.TextExtracted = true` and records event `Stage = ExtractText` with metrics (duration, byte count).
4. **Insight Stage**
   - For each chunk, `InsightSynthesisService` executes a templated `AI.Prompt` deriving structured facts; persists `DocumentInsight` and updates chunk references.
   - `VisionInsightService` runs for image files, using `AI.VisionPrompt` with diagram-specific instructions; stores insights tagged `Channel = Vision`.
5. **Template Suggestion**
   - `TemplateSuggestionService` computes embeddings for the document (averaged from chunks) and queries Weaviate. If disabled, fallback heuristics use TF-IDF and metadata.
   - Suggestion stored in `SourceDocument.Summary.AutoClassificationConfidence` and appended to processing events.
6. **Aggregation & Completion**
   - `InsightAggregationService` optionally produces a consolidated summary (`PrimaryFindings`), ensures `SourceDocument.Status = Completed`, emits final event, and publishes a Koan domain notification for UI/webhooks.

### 4. Refactoring Steps

1. **Establish Work Item Contract**
   - Define `DocumentWorkItem` (`DocumentId`, `AssignedProfileId`, `RetryCount`, `TraceId`).
   - Configure DI to register a singleton `Channel<DocumentWorkItem>` with bounded capacity derived from configuration (`Processing:QueueLimit`).

2. **Hosted Pipeline**
   - Implement `DocumentAnalysisPipeline : BackgroundService`; pull items using `ReadAllAsync`, wrap each stage in try/catch with exponential backoff and max retries from configuration.
   - Record `DocumentProcessingEvent` before and after each stage, capturing metrics and errors.

3. **Service Composition**
   - Move extraction helpers into dedicated classes. Each service exposes async methods returning strongly typed DTOs (`ExtractionResult`, `InsightBatch`, `TemplateSuggestion`).
   - Register services via `S13DocMindRegistrar` so `Program.cs` simply calls `builder.Services.AddKoan().AddDocMind();`.

4. **AI Prompt Strategy**
   - Store canonical prompt fragments in `SemanticTypeProfile.Prompt` and `TemplateExtractionSchema`.
   - Provide default fallback prompts for unassigned documents in the registrar.
   - Ensure all AI calls set model + temperature via configuration keys (`Ai:DefaultModel`, `Ai:VisionModel`).

5. **Observability Enhancements**
   - Emit structured logs with `LoggerMessage` source generators for each stage.
   - Publish OpenTelemetry spans (`ActivitySource`) around long-running AI calls.
   - Surface `DocumentProcessingEvent` query endpoints (`GET /api/documents/{id}/timeline`).

6. **Error Handling & Retries**
   - Distinguish between transient (`HttpRequestException`, `TaskCanceledException`) and terminal errors; push transient failures back onto the channel with incremented `RetryCount`.
   - If retries exceed configured limit, set `SourceDocument.Status = Failed` and record the error payload for UI display.

7. **Performance Optimizations**
   - Batch embedding generation by accumulating chunk texts and issuing a single `AI.Embed()` call per document.
   - Cache OCR results for repeated images using storage object hash as key.
   - Allow pipeline parallelism via configuration (`Processing:MaxDegreeOfParallelism`) controlling the number of concurrent document tasks.

### 5. Opportunities for Developer Experience Improvements

- **Replay tooling**: Provide a CLI (`dotnet run -- project S13.DocMind replay --document <id>`) that pushes a historical document back onto the queue, simplifying demos.
- **Simulation fixtures**: Ship sample PDF/DOCX/PNG fixtures plus an integration test harness that invokes the pipeline end-to-end using in-memory storage providers.
- **Prompt playground**: Expose a protected `/api/templates/{id}/prompt-test` endpoint that executes a dry-run prompt against a chosen chunk, enabling quick iteration without rerunning the full pipeline.
- **MCP automation**: Implement an MCP tool `docmind.process` that enqueues documents via the same work item contract, showcasing how Koan MCP integrates with background processing.

### 6. UI Alignment Notes

- Update Angular polling logic to consume the new `DocumentProcessingEvent` timeline, enabling timeline visualizations and toast notifications.
- Provide chunk-level insight display with lazy loading (`GET /api/documents/{id}/chunks?includeInsights=true`).
- Surface template suggestions in a dedicated side panel, supporting “accept suggestion” actions that call `POST /api/documents/{id}/assign-profile`.

This plan keeps orchestration lightweight while showcasing Koan’s AI capabilities, resilience patterns, and observability in a way that aligns with the minimal stack requirement.
