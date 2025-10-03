---
type: GUIDE
domain: ai
title: "AI pipeline and processing lanes"
audience: [architects, developers]
status: current
last_updated: 2025-02-18
framework_version: v0.6.2
validation:
	date_last_tested: 2025-02-18
	status: verified
	scope: samples/S13.DocMind
---

# AI pipeline and processing lanes

**Contract**

- **Input**: Document upload with metadata stored on `SourceDocument`.
- **Output**: Chunked text, structured insights, optional embeddings, and diagnostic events.
- **Error modes**: Model declines, adapter timeouts, vector service offline, or pipeline stage retries.
- **Success criteria**: Background worker completes flow with consistent status transitions and fallbacks.

**Edge cases**

- If Ollama declines (`AIResult.IsDeclined`), the pipeline records the decline and emits a neutral insight so UI timelines remain coherent.
- Embedding generation runs only when `Vector<DocumentChunkEmbedding>.IsAvailable` returns `true`.
- Replays must reset stage markers to avoid duplicate insight rows; use idempotent `DocumentProcessingEvent` updates.

## Processing sequence

1. **Ingress** – `DocumentIntakeService` saves the `SourceDocument`, enqueues a `DocumentProcessingJob`, and emits an `Upload` stage event.
2. **Text extraction** – `TextExtractionService` chunkifies the binary with paragraph heuristics, estimating token counts per chunk.
3. **Insight synthesis** – `InsightSynthesisService` composes prompt context from the chunk, semantic profile, and timeline state. Fallback messages persist when the AI provider declines to answer.
4. **Vision enrichment** – Conditional `VisionInsightService` adds metadata and commentary for image pages.
5. **Aggregation** – `InsightAggregationService` compiles document-level narratives and updates `DocumentProcessingSummary`.
6. **Vector enrichment** – When enabled, `EmbeddingProjectionService` persists `DocumentChunkEmbedding` and `SemanticTypeEmbedding` rows for discovery features.

Each step raises `DocumentProcessingEvent` entries (`ProcessingStage` enum) so operators can trace progress.

## Hosted worker orchestration

- `DocumentProcessingWorker` inherits from `BackgroundService`, polling `DocumentProcessingQueue` for jobs.
- The worker uses scoped pipelines (`IDocumentPipeline`) per semantic profile. Pipelines compose the services above via constructor injection.
- Retries delay exponentially and cap at 5; fatal failures mark the document status as `Failed` but retain partial insights for forensics.

## Controllers and APIs

- `DocumentsController` exposes upload, detail view, chunk listing, and timeline endpoints.
- `InsightsController` accepts manual re-analysis requests and surfaces aggregate insights.
- `ProcessingController` returns queue depth, configuration details, and recent events for the diagnostics panel.
- `TemplatesController` manages `SemanticTypeProfile` catalogues and ensures prompt fragments remain consistent between web and MCP clients.

## MCP exposure

The sample annotates entities with `[McpEntity]`, enabling HTTP SSE MCP clients to:

- Stream `SourceDocument` status transitions.
- Request top insights per document or per semantic profile.
- Trigger manual analyses via MCP commands mapped to controller actions.

## Extensibility hooks

- Swap `IInsightSynthesizer` implementations to target external LLMs with the same request envelope.
- Introduce batch ingestion by replacing the queue with Koan Flow events; keep the command handlers intact.
- Layer RBAC around manual analysis endpoints by decorating controllers with Koan auth attributes.
