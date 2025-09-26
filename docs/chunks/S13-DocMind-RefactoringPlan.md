# S13.DocMind Refactoring Plan

## Overview
This plan rebuilds the S13.DocMind sample from its foundational data contracts up through orchestration, AI enrichment, and API exposure. It addresses the gaps identified between the proposal and the current implementation by iterating through dependency layers—establishing bedrock entities and storage rules, then layering in infrastructure, services, and UX-facing processes.

## 1. Bedrock Layer – Domain & Persistence Contracts
1. **Entity reconciliation**
   - Replace legacy `File`, `Analysis`, and `DocumentType` usage with the proposal's `SourceDocument`, `DocumentChunk`, `DocumentInsight`, and `DocumentProcessingEvent` entities.
   - Introduce supporting value objects (`DocumentProcessingSummary`, insight payload models) and enums (`ProcessingStage`, `ProcessingStatus`).
2. **Event schema completion**
   - Extend `DocumentProcessingEvent` with `Message`, `Context`, `CreatedAt`, and correlation identifiers.
   - Define repository abstractions and Koan adapters for persisting these events.
3. **Vector adapter alignment**
   - Add `[VectorAdapter("weaviate")]` entities (`DocumentChunkEmbedding`, `SemanticTypeEmbedding`).
   - Remove inline `[Vector]` fields from core entities and introduce relationships to vector records.
4. **Configuration & options baseline**
   - Create `DocMindOptions` with sections for storage, AI models, and processing thresholds.
   - Ensure configuration is loaded via `AddOptions().BindConfiguration("DocMind")`.

## 2. Infrastructure Layer – Initialization & Cross-Cutting Services
1. **Koan auto-registration**
   - Replace manual DI registration in `Program.cs` with `AddKoan().AddKoanMcp()` and invoke a new `KoanAutoRegistrar`.
   - Move pipeline registrations, hosted workers, and options wiring into the registrar.
2. **Storage and adapters**
   - Configure MongoDB (documents), Weaviate (vectors), and optional cache providers via Koan adapters.
   - Implement blob/file storage abstractions consistent with the proposal.
3. **Queue & background orchestration plumbing**
   - Define `DocumentPipelineQueue` compatible with the new `DocumentWorkItem` schema (stage, status, entity IDs).
   - Register hosted worker(s) for intake and processing stages.
4. **Observability foundations**
   - Implement persistence-backed `DocumentProcessingEventLogger` that emits diagnostics hooks and integrates with Koan tracing/metrics.

## 3. Data Access & Aggregation Layer
1. **Repositories and query surfaces**
   - Provide query helpers for `SourceDocument`, `DocumentChunk`, `DocumentInsight`, and embedding entities using Koan's `Entity\<T>` APIs.
   - Build aggregation utilities for timeline views and insight summaries.
2. **Insight collections**
   - Materialize `InsightCollection` and `SimilarityProjection` aggregates per proposal for analytics scenarios.
   - Schedule background refresh jobs if needed, leveraging Koan data flows.
3. **Testing & fixtures**
   - Author unit tests that validate entity mappings, vector adapter interactions, and query helpers with in-memory providers.

## 4. Application Services Layer
1. **Document intake service**
   - Refactor to create `SourceDocument` records, enqueue `DocumentWorkItem`s, and log persisted intake events.
   - Normalize file validation, storage upload, and metadata extraction.
2. **Analysis pipeline**
   - Rewrite pipeline stages to hydrate `DocumentChunk`s, generate `DocumentInsight`s, and update `DocumentProcessingSummary`.
   - Remove references to deprecated properties (e.g., `Summary`, `Suggestions`), using the new value objects.
3. **Event publishing**
   - Emit `DocumentProcessingEvent`s at each stage with contextual payloads for timeline experiences.
4. **Insight synthesis**
   - Implement structured prompt flows for summarization, template extraction, and classification aligned with the `DocumentInsight.StructuredPayload` contract.

## 5. AI & Intelligence Layer
1. **Text extraction & OCR**
   - Integrate `IAi.VisionPrompt` for image/PDF extraction, storing results within `DocumentChunk`s and capturing extraction metadata.
2. **Vision analysis**
   - Build `VisionInsightService` atop Koan AI prompts with configurable models, capturing confidence scores and tagging with the `Vision` channel.
3. **Embedding workflow**
   - Use Koan vector adapters to create embeddings for chunks and semantic types; ensure retry/backoff and adapter health checks.
4. **Template suggestion & classification**
   - Refactor to query Weaviate via adapters; provide lexical fallbacks and unify scoring logic for insight recommendation.

## 6. Interface & Experience Layer
1. **API controllers**
   - Update controllers to consume the new entities, vector-backed services, and persisted processing events (timeline endpoints).
   - Ensure response DTOs align with MCP and Angular contracts while sourcing data from new aggregates.
2. **Diagnostics & timeline UX**
   - Back controllers and UI components with the persisted event store; expose filters for stage/status/context.
3. **Documentation & samples**
   - Refresh docs and samples to reflect the new bootstrap pattern, configuration hierarchy, and data flow diagrams.

## 7. Process & Quality Layer
1. **Migration strategy**
   - Provide scripts or notes for clearing legacy collections and initializing new adapters (Mongo/Weaviate indices).
   - Document environment variables/secrets required for AI and vector adapters.
2. **Testing matrix**
   - Define automated test suites: unit (services), integration (pipeline & adapters), functional (end-to-end document run).
3. **Operational runbook**
   - Capture monitoring hooks, alert thresholds, and troubleshooting steps tied to the new event logging and vector dependencies.
4. **Release checklist**
   - Ensure registrar wiring, configuration, migrations, and smoke tests are validated before shipping the updated sample.

## Execution Ordering
1. Complete **Bedrock** and **Infrastructure** layers to stabilize entity contracts and runtime wiring.
2. Proceed with **Data Access** and **Application Services** refactors to rebuild the pipeline on the new foundation.
3. Layer on **AI & Intelligence** enhancements, followed by **Interface** updates to surface the richer capabilities.
4. Close with **Process & Quality** deliverables to support operations, documentation, and sustainable maintenance.
