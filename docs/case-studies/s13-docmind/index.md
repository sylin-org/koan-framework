---
type: GUIDE
domain: ai
title: "S13.DocMind case study"
audience: [architects, developers]
status: current
last_updated: 2025-02-18
framework_version: v0.6.2
validation:
	date_last_tested: 2025-02-18
	status: verified
	scope: samples/S13.DocMind
---

# S13.DocMind case study

**Contract**

- **Audience**: Architects and senior engineers evaluating Koan patterns for document-intelligence workloads.
- **Inputs**: Familiarity with Koan entity-first development, hosted services, and AI abstractions. Access to the `samples/S13.DocMind` solution.
- **Outputs**: Repeatable blueprint for ingestion → chunking → AI insight pipelines, plus references to detailed artefacts.
- **Error modes**: Misconfigured vector providers, oversized uploads, or missing AI adapters. Each section below calls out fallback behavior.
- **Success criteria**: Teams can replay the sample end-to-end, understand the critical entities/services, and adapt the pattern to their own workloads.

**Edge cases to watch**

1. Uploads beyond 10 MB require chunk-size tuning and storage provider overrides.
2. Vector enrichment is optional; fallbacks must still return deterministic summaries.
3. MCP exposure expects Koan HTTP SSE hosting; disable the annotation if your environment cannot accept long-lived connections.
4. External AI providers may throttle aggressively—ensure rate-limit headers propagate through the diagnostics pipeline.
5. Workshop environments often skip GPU containers; keep Ollama prompts constrained for CPU inference or swap to cloud providers via configuration.

## Narrative

S13.DocMind demonstrates how Koan composes data adapters, AI services, and hosted workers into a document-intelligence loop. The curated content here replaces the earlier raw chunk exports; deep research artefacts now live under [`/docs/archive/chunks/S13-DocMind/`](../../archive/chunks/S13-DocMind/).

| Phase | Highlights | References |
|-------|------------|------------|
| **Intake** | Entity-first upload flow, immediate dedupe via SHA-512, background orchestration kick-off | [`data-modeling.md`](data-modeling.md) |
| **Processing** | Hosted job sequencer, chunk projection, insight synthesis with graceful fallbacks | [`ai-pipeline.md`](ai-pipeline.md) |
| **Discovery** | Vector-enabled search when available, MCP surface for agents, UI-aligned controllers | [`operations-and-diagnostics.md`](operations-and-diagnostics.md) |

## Quick start

1. `./start.bat` from `samples/S13.DocMind` to launch API, MongoDB, Weaviate (optional), and Ollama.
2. Navigate to the Angular client at `http://localhost:5105` and upload sample documents from `samples/S13.DocMind/sample-data`.
3. Use the diagnostics dashboard to inspect `DocumentProcessingEvent` timelines and confirm AI fallbacks fire when Ollama declines prompts.
4. Attach an MCP client (Koan CLI or compatible) to the HTTP SSE endpoint advertised in the boot report to explore documents and insights.

## When to extend

- **Higher volume ingest**: Swap `DocumentProcessingWorker` for a Topic-backed flow (Koan Flow) and reuse the same service collaborators.
- **Enterprise governance**: Layer on multi-tenant storage providers and emit policy violations via `DocumentProcessingEvent` with severity markers.
- **Specialized models**: Inject custom `IInsightSynthesizer` implementations; the hosted worker dispatches by semantic profile code.

## Related reading

- [`Guides: Data modeling`](../../guides/data-modeling.md)
- [`Reference: Web module`](../../reference/web/index.md)
- [`ARCH-0055: Koan Aspire integration approval`](../../decisions/ARCH-0055-koan-aspire-integration-approval.md)
