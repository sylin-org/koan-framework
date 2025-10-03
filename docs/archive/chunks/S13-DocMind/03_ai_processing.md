## **AI Processing & Pipeline Refactoring Plan**

### 1. Guiding Principles

- **Simple hosted services**: Use standard `BackgroundService` patterns for document processing; avoid channel queues and complex orchestration.
- **Composable services**: Split the current monolithic analysis service into focused collaborators that map 1:1 with the new domain models.
- **Single AI provider**: Rely on Ollama with multiple model support through Koan AI abstractions (`AI.Prompt`, `AI.VisionPrompt`, `AI.Embed`).
- **Observable by default**: Emit `DocumentProcessingEvent` entries for every stage transition and surface them via API + MCP tools for debugging and UX timelines.

### 2. Proposed Processing Components

| Component | Responsibility | Key Dependencies | Notes |
|-----------|----------------|------------------|-------|
| `DocumentProcessor` | Main document processing orchestrator | Koan AI, storage providers | Single hosted service handling document lifecycle. |
| `TextExtractionService` | Extracts textual content from PDFs, DOCX, text, images | PdfPig, OpenXML, OCR provider | Writes `DocumentChunk` records and updates `Summary`. |
| `VisionInsightService` | Runs `AI.VisionPrompt` for diagrams/screenshots | Koan AI Vision | Produces `DocumentInsight` entries flagged as `Vision`. |
| `InsightSynthesisService` | Generates structured facts from text chunks | Koan AI text models | Operates per chunk, merges into summary. |
| `TemplateSuggestionService` | Suggests `SemanticTypeProfile` matches | Koan embeddings + Weaviate adapter | Falls back to lexical heuristics when embeddings disabled. |
| `DocumentProcessingWorker` (Hosted Service) | Background worker monitoring for new documents | `ILogger`, Koan AI | Simple polling-based processing without channels. |

### 3. Processing Flow

1. **Upload** – `DocumentsController.Upload` validates file size/type, streams to storage, creates `SourceDocument` with status `Uploaded`.
2. **Background Detection** – `DocumentProcessingWorker` polls for documents with status `Uploaded` and triggers processing.
3. **Processing Orchestration** – `DocumentProcessor` handles the complete workflow:
   - **Text Extraction**: `TextExtractionService` extracts content and creates `DocumentChunk` entities
   - **Vision Analysis**: `VisionInsightService` analyzes images using `AI.VisionPrompt`
   - **Insight Generation**: `InsightSynthesisService` generates structured insights per chunk
   - **Template Suggestion**: `TemplateSuggestionService` suggests matching semantic types
   - **Status Updates**: Updates `SourceDocument.Status` and creates `DocumentProcessingEvent` entries
4. **Completion** – Sets `SourceDocument.Status = Completed` and updates summary information.

### 4. Refactoring Steps

1. **Background Worker Implementation**
   - Implement `DocumentProcessingWorker : BackgroundService` that polls for documents with status `Uploaded`
   - Use simple timer-based polling instead of complex channel orchestration
   - Record `DocumentProcessingEvent` entries for each processing stage

2. **Document Processor Service**
   - Create `DocumentProcessor` as the main orchestration service
   - Handle complete document lifecycle from upload to completion
   - Coordinate all extraction, analysis, and insight generation services

3. **Service Composition**
   - Move extraction helpers into dedicated classes with clear responsibilities
   - Each service exposes async methods returning strongly typed DTOs (`ExtractionResult`, `InsightBatch`, `TemplateSuggestion`)
   - Register services via `KoanAutoRegistrar` for automatic discovery

4. **AI Integration Strategy**
   - Store canonical prompt fragments in `SemanticTypeProfile.Prompt` and `TemplateExtractionSchema`
   - Use single Ollama provider with multiple model support via configuration
   - Configure models via `DocMind:Ai:DefaultModel` and `DocMind:Ai:VisionModel`

5. **Observability Enhancements**
   - Emit structured logs with `LoggerMessage` source generators for each stage
   - Persist `DocumentProcessingEvent` entries and expose them via `ProcessingController` queue/timeline queries
   - Extend the boot report with stage counts and provider readiness derived from `DocumentProcessingDiagnostics`

6. **Error Handling**
   - Simple retry logic with configurable retry counts
   - Set `SourceDocument.Status = Failed` for terminal errors
   - Record error details in `DocumentProcessingEvent` entries

7. **Performance Optimizations**
   - Batch embedding generation for efficient AI calls
   - Cache OCR results using storage object hash as key
   - Configure processing concurrency via `DocMind:Processing:MaxConcurrency`

### 5. Opportunities for Developer Experience Improvements

- **Replay tooling**: Provide a CLI (`dotnet run -- project S13.DocMind replay --document <id>`) that resets document status to trigger reprocessing.
- **Simulation fixtures**: Ship sample PDF/DOCX/PNG fixtures plus an integration test harness that invokes the pipeline end-to-end using in-memory storage providers.
- **Prompt playground**: Expose a protected `/api/templates/{id}/prompt-test` endpoint that executes a dry-run prompt against a chosen chunk, enabling quick iteration without rerunning the full pipeline.
- **MCP automation**: Implement MCP tools that trigger document processing and retrieve insights, showcasing how Koan MCP integrates with background services.

### 6. UI Alignment Notes

- Update Angular polling logic to consume the new `DocumentProcessingEvent` timeline, enabling timeline visualizations and toast notifications.
- Provide chunk-level insight display with lazy loading (`GET /api/documents/{id}/chunks?includeInsights=true`).
- Surface template suggestions in a dedicated side panel, supporting “accept suggestion” actions that call `POST /api/documents/{id}/assign-profile`.

This plan keeps orchestration simple using standard hosted services while showcasing Koan's AI capabilities and observability in a way that aligns with the minimal stack requirement.
