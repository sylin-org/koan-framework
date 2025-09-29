---
type: GUIDE
domain: data
title: "Data and domain model"
audience: [architects, developers]
status: current
last_updated: 2025-02-18
framework_version: v0.6.2
validation:
	date_last_tested: 2025-02-18
	status: verified
	scope: samples/S13.DocMind
---

# Data and domain model

**Contract**

- **Input**: Familiarity with Koan `Entity<T>` patterns and validation annotations.
- **Output**: Canonical entity set for document intake, processing, and diagnostics.
- **Error modes**: Missing vector adapters, oversized uploads, or absent semantic profiles.
- **Success criteria**: Teams can map the entities into their own solution and understand which services populate each model.

**Edge cases**

- Binary dedupe relies on SHA-512; ensure background workers requeue when a duplicate arrives with an updated profile assignment.
- Vision metadata is optional and should not block text extraction results.
- Embedding entities only activate when a vector provider is present; the API must tolerate null responses.

## Domain slices

| Domain | Responsibility | Key entities |
|--------|----------------|--------------|
| Intake | file metadata, dedupe receipts, upload guidance | `SourceDocument`, `DocumentUploadReceipt` |
| Templates & types | semantic profile catalogues and prompts | `SemanticTypeProfile`, `TemplateSection` |
| Processing | chunk projections and insight storage | `DocumentChunk`, `DocumentInsight`, `InsightCollection` |
| Diagnostics | stage transitions and replay hints | `DocumentProcessingEvent`, `ProcessingStage` |

## Core entities

- **`SourceDocument`** – stores immutable file descriptors, current processing status, and a `DocumentProcessingSummary` value object so UIs can render a snapshot without extra queries.
- **`SemanticTypeProfile`** – captures prompt templates, structured extraction schemas, and sample phrases. Profiles can be auto-assigned via heuristics during ingestion.
- **`DocumentChunk`** – one row per chunk, carrying the extracted text, token estimates, and references to generated insights.
- **`DocumentInsight`** – additive fact store keyed by `InsightChannel` (`Text`, `Vision`, `Aggregation`, `UserFeedback`). Designed for incremental enrichment across pipelines.
- **`DocumentProcessingEvent`** – timeline log covering each stage. Includes duration, token usage, and optional error payloads for diagnostics.

## Vector enablement (optional)

When Weaviate or another vector adapter runs, the sample activates two additional models:

- `SemanticTypeEmbedding` with `[VectorAdapter("weaviate")]` for template discovery.
- `DocumentChunkEmbedding` for content-based search. These entities honor Koan's provider discovery and stay dormant otherwise.

## Implementation notes

1. **Entity-first persistence**: No repositories. All write paths go through instance `Save()`; read paths use statics like `SourceDocument.AllStream()` for processing loops.
2. **Validation**: Size, MIME, and prompt requirements live on the entity via data annotations so API and MCP channels enforce the same rules.
3. **Telemetry**: Every state change emits a `DocumentProcessingEvent`; the UI and diagnostics controller query by document Id.
4. **Schema migrations**: Existing Mongo collections migrate lazily via boot tasks—new properties default cleanly, so older records hydrate without scripts.

## Next steps

- Review [`ai-pipeline.md`](ai-pipeline.md) for the hosted worker and AI collaborators that populate these models.
- Check [`operations-and-diagnostics.md`](operations-and-diagnostics.md) for surface areas consuming the telemetry data.
