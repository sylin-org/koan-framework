# S13.DocMind Next Steps

## Iteration 0 – Stabilize Contracts & Bootstrap
1. **Normalize entities** – Update `SourceDocument` and `DocumentChunk` to match the blueprint schema (storage metadata, processing summaries, removal of inline embeddings) and refactor services that still rely on legacy members.
2. **Vector adapter migration** – Introduce helper services for `DocumentChunkEmbedding`/`SemanticTypeEmbedding`, switch embedding persistence away from `[Vector]` fields, and validate Weaviate configuration in `DocMindOptions`.
3. **Adopt Koan bootstrap** – Move DocMind service registration into `DocMindRegistrar`, convert `Program.cs` to `AddKoan(...).AddKoanMcp()`, and bind options from the unified `DocMind` configuration section.

## Iteration 1 – Queue & Timeline Reliability
1. **Queue consolidation** – Remove the legacy service-layer queue, refit all producers/consumers to the infrastructure `DocumentPipelineQueue`, and enforce retry/backoff rules from `DocMindOptions.Processing`.
2. **Hosted worker hardening** – Ensure the background pipeline respects concurrency limits, records attempt telemetry, and gracefully handles cancellation/shutdown.
3. **Persistent events** – Replace log-based diagnostics with a repository-backed `IDocumentProcessingEventSink`, emit enriched events at intake and pipeline stages, and update diagnostics/timeline controllers to read from stored events.

## Iteration 2 – AI Enrichment & Discovery
1. **Extraction upgrades** – Integrate OCR/vision prompts, persist confidence metrics, and materialize multi-modal chunks that feed downstream insight synthesis.
2. **Insight synthesis rebuild** – Implement structured insight generation with prompt templates, mapping results into `DocumentInsight` entities and the new vector adapters.
3. **Discovery surface** – Refactor aggregation and suggestion services to query the normalized entities/vectors, updating API/MCP projections accordingly.

## Iteration 3 – Operational Readiness
1. **Testing** – Add unit/integration tests covering intake dedupe, pipeline progression, embedding persistence, and API endpoints using Koan testing facilities.
2. **Observability** – Capture model usage metrics, token counts, and failure diagnostics via the event sink and Koan observability modules.
3. **Documentation & tooling** – Refresh README/MCP docs, provide setup scripts for MongoDB/Weaviate, and document configuration for supported AI providers.
