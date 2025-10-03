# ADR-XXXX — Rebooting Koan Backup Architecture

**Status**: Proposed  
**Date**: 2025-10-03  
**Authors**: Codex (Software Architect)

## Context

Koan.Data.Backup implements the framework’s vision for zero-configuration backup and restore (docs/design/BACKUP-SYSTEM.md). The implementation delivers streaming exports via `StreamingBackupService`, manifests through `BackupStorageService`, entity discovery via `EntityDiscoveryService`, and restore orchestration using `OptimizedRestoreService`. A “Full Stack Backup/Restore” integration test (`FullStackBackupRestoreTests`) exercises the pipeline against SQLite and MongoDB containers.

During recent verification:
- The integration test fails because no backup archive is found under the expected path and the manifest reports zero exported items.
- Inspection of backup code reveals path mismatches, discovery gaps, memory-heavy streaming, and brittle provider metadata assumptions.
- Several stop-gap fixes would not resolve structural risk; a redesign is preferred before exposing backup publicly.

## Problems

1. **Incorrect archive naming**
   - `CreateBackupArchiveAsync` prefixes keys with `"backups/"` (src/Koan.Data.Backup/Storage/BackupStorageService.cs:33-37) while `UploadBackupArchiveAsync` already scopes the container to `"backups"`. Uploaded files end up under `backups/backups/...`, but tests and consumers look for `<base>/backups/<name>.zip`, yielding false negatives and orphaned data.

2. **Discovery and metadata gaps**
   - `EntityDiscoveryService` invents placeholder `DataAdapterAttribute`/`SourceAdapterAttribute` (src/Koan.Data.Backup/Core/EntityDiscoveryService.cs:242-253) instead of using real Koan metadata. Provider and partition information degrade to `"default"` and `"root"`, meaning the backup never routes to the same partitions used for writes.
   - Discovery scans every assembly for `IEntity<>` (src/Koan.Data.Backup/Core/EntityDiscoveryService.cs:52-78), including abstract or unused types, but never confirms entities have persisted data. Manifest entries therefore list many types with `ItemCount = 0`.

3. **Non-streaming archive writer**
   - Backups accumulate an entire ZIP in a single `MemoryStream` before upload, producing quadratic memory growth and preventing streaming to remote providers.

4. **Partition-unaware backup**
   - `StreamingBackupService` hardcodes `"root"` when exporting data via reflection (src/Koan.Data.Backup/Core/StreamingBackupService.cs:330). Applications using `EntityContext.Partition`, tenant routing, or named sets will lose data.

5. **Incomplete restore contract**
   - Restore uses reflection-based `UpsertMany` calls without verifying constraints/index restores succeeded or reconciling partitions. Error handling is best-effort, but not transactional. Vector data support is stubbed only in docs.

6. **Test harness divergence**
   - Integration tests assume the simple `<temp>/backups/...` layout (tests/Koan.Data.Backup.Tests/RealWorld/FullStackBackupRestoreTests.cs:263-270). The mismatch hides logical errors and yields repeated overnight crawler cleanup of misnamed archives.

## Goals

- Provide a resilient, memory-safe backup/restore pipeline that aligns with Koan provider transparency and partition semantics.
- Produce verifiable manifests with accurate counts and file metadata.
- Offer predictable archive naming and discovery APIs for operators and tooling.
- Ground discovery in real Koan metadata; eliminate assumptions about attributes or partitions.
- Maintain extensibility for vectors, constraint management, and remote storage.

## Proposed Direction

1. **Archive Naming & Storage Contract**
   - Introduce a `BackupArchiveNaming` helper that consumes manifest metadata (backup name, CreatedAt, id) and produces:
     - `StorageKey` (relative path within the storage profile, e.g., `fullstack-test/2025/10/03/<timestamp>.zip`).
     - `DisplayPath` for file system mirrors/tests (e.g., `<base>/backups/<storage key>`).
   - `CreateBackupArchiveAsync` should return only a filename (no container prefix). Container scoping remains the responsibility of `IStorageService`.
   - Record the selected storage key inside the manifest (`manifest.StoragePath`) to avoid recomputing paths later.

2. **Discovery Refactor**
   - Replace reflection-only scanning with `AggregateConfigs` and Koan metadata providers:
     - Query `AggregateConfigs` cache for active entities (ensures repository has been initialized).
     - For each entity, gather provider, partition support, and capabilities using `Data<T>` plus `EntityContext` metadata.
     - Add support for vector-capable entities by querying vector registries.
   - Cache discovery results with a version stamp so warmup/backup can reuse metadata without rescanning AppDomain.

3. **Streaming Writer Infrastructure**
   - Swap the single `MemoryStream` with a streaming archive writer:
     - Option A: `ZipArchive` over a temporary file with chunked uploads to storage.
     - Option B: Provide a streaming `IStorageProvider` sink (e.g., S3 multipart) and write directly.
   - Implement a `BackupArchiveWriter` abstraction that streams JSONL chunks (`IAsyncEnumerable<T>`) into per-entity entries while monitoring size and progress.
   - Capture per-entry metrics (counts, bytes, hashes) incrementally to avoid storing all rows in memory.

4. **Partition & Provider Awareness**
   - Discovery must enumerate known partitions per entity (from Koan set routing metadata or configuration).
   - Backup loop iterates partitions: for each entity + partition, set `EntityContext.Partition` (or use appropriate provider override) before streaming data.
   - Manifest entries should include `Partition`, `Provider`, `StorageFile`, and counts per partition.

5. **Robust Restore**
   - Extend manifests with schema info, partitions, and optional transformation hints.
   - During restore, rehydrate per partition, apply `EntityContext.Partition`, and leverage optimized repository hooks for constraint/index management.
   - Provide dry-run verification: read manifest + metadata without writing to confirm a backup is restorable.

6. **Operator APIs & Tooling**
   - Expose consistent discovery endpoints (`/backup/capabilities`, `/backup/catalog`) using the new metadata.
   - Provide CLI/SDK helpers for listing archives and verifying manifests.
   - Integrate integrity checks (manifest checksum validation, optional hash re-computation) before restore.

## Plan

1. **Foundational Cleanup (Sprint 1)**
   - Update `BackupArchiveNaming` and storage key handling.
   - Fix `CreateBackupArchiveAsync` / `UploadBackupArchiveAsync` contract.
   - Align integration tests with new naming convention and ensure file system storage provider persists `StorageObject.Size`.

2. **Discovery & Metadata (Sprint 2)**
   - Replace placeholder attributes with real Koan metadata lookups.
   - Populate manifest with accurate provider, partition, and capability info.
   - Introduce manifest schema versioning.

3. **Streaming Writer (Sprint 3)**
   - Implement `BackupArchiveWriter` that supports streaming to disk or provider sinks.
   - Add progress callbacks per entity/partition.
   - Stress-test with large datasets to validate memory profile.

4. **Restore Pipeline (Sprint 4)**
   - Update `OptimizedRestoreService` to iterate manifest entries per partition and use the new metadata.
   - Enhance error handling and rollback strategies (e.g., staging vs. direct upsert).
   - Add integrity checks before and after restore.

5. **UX & API Refinement (Sprint 5)**
   - Update REST endpoints, CLI stubs, and docs to reflect the reworked system.
   - Provide operator guides for verifying backups and performing restores.
   - Validate vector support or explicitly flag as future work.

## Decision

- Proceed with a ground-up refactor of Koan.Data.Backup in line with the proposed direction. Short-term hotfixes (path correction alone) are insufficient; adoption should wait until the above plan is complete.

## Consequences

- Backup/restore code will undergo significant change; downstream consumers must expect updated manifest formats and APIs.
- Integration tests will initially fail; they should be updated once each phase lands.
- Storage providers and adapters may require minimal updates to surface constraint/optimization hooks.

## Open Questions

- What is the preferred remote storage streaming interface (multipart vs. chunked file writes)?
- How should vector embeddings be serialized for cross-provider restores? (Design mentions caches, but implementation is TBD.)
- Do we need transaction-like guarantees for restore (e.g., stage all partitions before committing)?

