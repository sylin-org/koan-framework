---
type: GUIDE
domain: orchestration
title: "Operations and diagnostics"
audience: [architects, developers]
status: current
last_updated: 2025-02-18
framework_version: v0.6.2
validation:
	date_last_tested: 2025-02-18
	status: verified
	scope: samples/S13.DocMind
---

# Operations and diagnostics

**Contract**

- **Input**: Running S13.DocMind environment (API, MongoDB, optional Weaviate, Ollama).
- **Output**: Repeatable operational playbook for workshops, demos, and troubleshooting.
- **Error modes**: Container misalignment, MCP transport disconnects, or long-running jobs.
- **Success criteria**: Operators can verify health, inspect pipelines, and recover from failures with minimal toil.

**Edge cases**

- Containerized labs occasionally omit Weaviate; dashboards should hide vector features gracefully.
- When MCP is disabled, remove `[McpEntity]` attributes or configure KoanEnv to suppress registration warnings.
- Large documents can exceed SSE message limits; prefer `AllStream(batchSize)` queries when exporting data.

## Observability toolkit

- **Boot report** – Koan boot report lists detected modules (`mongo`, `weaviate`, `ollama`, MCP endpoints). Capture it for workshop notes.
- **Processing timeline** – `ProcessingController.GetTimeline` renders `DocumentProcessingEvent` history for the Angular diagnostics panel.
- **Queue stats** – `ProcessingController.GetQueueHealth` highlights backlog growth and retry counts.
- **AI telemetry** – Insight synthesizers log token counts per stage; surface them via `DocumentProcessingEvent` payloads.

## Recovery patterns

1. **Document retry** – `ProcessingController.RetryDocument` resets status to `Queued` and republishes the job.
2. **Rebuild aggregations** – `ProcessingController.RebuildAggregate` regenerates summaries without re-uploading files.
3. **Vector cache reset** – Drop `SemanticTypeEmbedding` and `DocumentChunkEmbedding` entities if Weaviate falls out of sync; they auto-regenerate on next analysis.
4. **Manual insights** – `InsightsController.RunManualAnalysis` lets SMEs annotate documents and stores results alongside AI output.

## Workshop checklist

- Verify `KoanAutoRegistrar` registers hosted services and vector providers in the boot report.
- Seed sample data via `samples/S13.DocMind/sample-data/seed.ps1` if time-constrained.
- Use the `Docs` MCP command pack to demonstrate agent-driven retrieval.
- Share the [`ai-pipeline.md`](ai-pipeline.md) diagram when explaining background flow to participants.

## Extension roadmap

- Promote vector search to a first-class guide once additional providers reach parity.
- Integrate cost-tracking metrics by emitting `DocumentProcessingEvent` entries tagged with billing dimensions.
- Publish an MCP-only walkthrough describing how agents request insights without the Angular client.
