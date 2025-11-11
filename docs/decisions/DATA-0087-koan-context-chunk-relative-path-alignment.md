---
id: DATA-0087
slug: DATA-0087-koan-context-chunk-relative-path-alignment
domain: DATA
status: Accepted
date: 2025-11-11
title: Koan.Context Chunk Metadata Relative Path Alignment
---

| **Contract** | |
| --- | --- |
| **Inputs** | Koan.Context indexing pipeline (`Indexer`, `IncrementalIndexer`, legacy `IndexingService`), `Extraction.ExtractAsync`, persisted `IndexedFile` and `Chunk` tables, transactional outbox (`SyncOperation`). |
| **Outputs** | Consistent relative-path metadata on `ExtractedDocument`, `Chunk`, and vector payloads; stable chunk maintenance that avoids deleting healthy embeddings. |
| **Error Modes** | Partition maintenance deleting chunks due to perceived orphaning; vector sync over-count from replayed `SyncOperation` records; semantic search queries returning zero results after reindex. |
| **Success Criteria** | Forced/ incremental reindex preserves chunk/vector counts; no orphan cleanup when manifest and chunk data agree; `ChunkMaintenanceService` no longer flushes valid data; regression tests/builds pass. |

## Summary

Koan.Context stored chunk metadata with file-name-only paths because `Extraction.ExtractAsync` defaulted `ExtractedDocument.RelativePath` to `Path.GetFileName(filePath)`. The indexing manifest persisted full relative paths (for example `docs/guides/foo.md`). When differential planning compared manifest entries with chunk records, every chunk appeared orphaned and `ChunkMaintenanceService` deleted both the relational metadata and the associated vectors. Forced reindex then replayed stale outbox operations, inflating vector sync counters and wiping semantic search results.

## Findings

- `Indexer` and `IncrementalIndexer` obtain the correct relative path before invoking extraction, but the extractor discarded it.
- `Chunk` rows therefore stored only the terminal file name, while `IndexedFile.RelativePath` remained scoped to the project root.
- `IndexingPlanner` flagged all chunks as orphaned, triggering `ChunkMaintenanceService.RemoveFilesAsync` to delete valid data and the newly added outbox cleanup to purge related `SyncOperation` rows.
- Vector sync metrics became inconsistent: jobs reported far more vectors synced than chunks created and subsequent searches returned zero hits.

## Decision

Align extraction, chunk persistence, and vector metadata on the same relative path string so manifest reconciliation remains accurate and maintenance routines only delete genuine orphans.

## Implementation

- Extend `Extraction.ExtractAsync` with an overload that accepts an explicit `relativePath` and threads it through every `ExtractedDocument` instance. The legacy signature now delegates to the overload to preserve call-site compatibility.
- Update `Indexer`, `IncrementalIndexer`, and the legacy `Koan.Context` `IndexingService` to pass their computed relative paths into the extractor.
- With matching metadata, orphan detection no longer misfires, so chunk maintenance leaves healthy data intact and vector sync metrics stabilize.

## Edge Cases Considered

1. **Root-level files** – If a file sits at the project root, the computed relative path equals the file name; the new overload preserves that value without trimming.
2. **Subdirectory fan-out** – Deeply nested paths (e.g., `samples/guides/demo.md`) persist verbatim, preventing premature cleanup when only a subset of directories changes.
3. **Forced reindex restarts** – Cancelled jobs now finish removing pending outbox entries before the restart, ensuring replayed operations map back to existing chunks.
4. **Incremental watcher updates** – File system change notifications reuse the same extractor overload, keeping maintenance deterministic during continuous indexing.
5. **Whitespace-only documents** – Empty or whitespace files still short-circuit the extractor, but they now record the caller-supplied relative path to aid diagnostics.

## Verification

- `dotnet build src/Services/code-intelligence/Koan.Service.KoanContext/Koan.Service.KoanContext.csproj`
- Manual forced reindex on a test project confirmed chunk/vector counts hold steady and the maintenance log no longer reports mass orphan removals.

## Follow-up

- Add integration coverage that reindexes a project twice and asserts the chunk count remains stable.
- Monitor `VectorSyncWorker` metrics to confirm vector sync deltas stay aligned with chunk counts across restarts.
