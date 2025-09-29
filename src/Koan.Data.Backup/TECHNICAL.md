---
uid: reference.modules.koan.data.backup
title: Koan.Data.Backup – Technical Reference
description: Streaming backup and restore services with discovery, storage integration, and maintenance workflows for Koan entities.
since: 0.6.3
packages: [Sylin.Koan.Data.Backup]
source: src/Koan.Data.Backup/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide streaming-first backup and restore services that operate on Koan entities via `Data<TEntity, TKey>` statics and adapter metadata.
- Auto-register backup plans, discovery services, and optional maintenance jobs when the package is referenced so applications obtain turnkey backup APIs.
- Persist manifests, verification data, and entity payloads through Koan storage providers while exposing progress, viability checks, and catalog discovery.
- Support selective restore paths with optimization hooks (`IRestoreOptimizedRepository`) so adapters can disable constraints, batch, or bulk-load data safely.

## Key components

| Area                  | Types                                                                                          | Notes                                                                                                                                            |
| --------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| Registration & DI     | `Initialization.KoanAutoRegistrar`, `Extensions.ServiceCollectionExtensions`                   | Auto-wires services via `AddKoanBackupRestore*`, binds `BackupRestoreOptions`, and contributes capability notes to the boot report.              |
| Backup pipeline       | `StreamingBackupService`, `IBackupService`, `EntityDiscoveryService`, `BackupDiscoveryService` | Discovers entity metadata, streams records with `Data<TEntity, TKey>.AllStream`, builds manifests, and tracks progress in-memory.                |
| Restore pipeline      | `OptimizedRestoreService`, `IRestoreService`, `IRestoreOptimizedRepository`                    | Restores entities with optional adapter-specific preparation/cleanup, reflection fallbacks, and batch `UpsertManyAsync` calls.                   |
| Storage integration   | `BackupStorageService`, models under `Models/*`                                                | Writes JSON Lines payloads into ZIP archives, stores manifests & verification metadata, uploads via `IStorageService`, and reads during restore. |
| Background operations | `BackupMaintenanceService`                                                                     | Optional hosted service that warms discovery, refreshes catalogs, validates samples, and surfaces retention candidates based on options.         |
| Models & options      | `BackupManifest`, `BackupOptions`, `RestoreOptions`, `BackupRestoreOptions`, `BackupQuery`     | Provide manifest schema, per-operation knobs, and module-wide defaults (batch sizes, profiles, retention).                                       |

## Backup workflow

1. **Plan registration** – Applications register `BackupPlan` definitions (see README sample) or rely on discovery; `AddKoanBackupRestore()` wires services and options into DI.
2. **Entity discovery** – `EntityDiscoveryService` scans Koan aggregates, caching `EntityTypeInfo` records (entity type, key type, provider). Optional warmup occurs during maintenance startup when `WarmupEntitiesOnStartup` is enabled.
3. **Streaming export** – `StreamingBackupService` orchestrates backups:
   - Creates a manifest and progress record (stored in `_activeBackups`).
   - Builds a ZIP archive in memory via `BackupStorageService.CreateBackupArchiveAsync`.
   - Streams each entity using `Data<TEntity, TKey>.AllStream(batchSize)` (single-entity) or reflection-based enumeration for global backups.
   - Serializes records as JSON Lines and collects schema metadata, sizes, checksums, and timing metrics per entity.
   - After all entities are processed, writes manifest & verification files (`manifest.json`, `verification/*`) and uploads the archive using `IStorageService` under the configured storage profile.
4. **Progress & cancellation** – `GetBackupProgressAsync` returns aggregate metrics, while `CancelBackupAsync` marks the in-memory record as `Cancelled`. Backups run sequentially inside a single archive to avoid ZIP concurrency issues.

## Restore workflow

1. **Manifest loading** – `OptimizedRestoreService` locates backup archives through `BackupStorageService` and loads the stored `BackupManifest`.
2. **Adapter preparation** – If the entity repository implements `IRestoreOptimizedRepository`, `PrepareForRestoreAsync` receives `RestorePreparationOptions` (estimated counts, flags for disabling constraints/indexes, bulk mode). The returned context is restored after the operation.
3. **Data import** – Entities are read from stored JSON Lines via `BackupStorageService.ReadEntityDataAsync<T>` or reflection fallback (`ReadEntityDataAsObjects`). Records are upserted using `Data<TEntity, TKey>.UpsertManyAsync` in batches, respecting dry-run and continue-on-error settings.
4. **Progress & viability** – Global restores manage concurrent operations with a configurable `SemaphoreSlim`. `TestRestoreViabilityAsync` inspects manifests, checks entity types, and estimates restore time, capturing adapter optimization availability in a report.
5. **Error handling** – Failures per entity capture messages in `EntityBackupInfo.ErrorMessage` and log warnings without halting entire global runs unless `ContinueOnError=false`.

## Storage artifacts

- **Archive layout**: `entities/{EntityType}[#set].jsonl`, `manifest.json`, `verification/checksums.json`, `verification/schema-snapshots.json`.
- **Content format**: JSON Lines per entity with deterministic SHA-256 content hashes. Overall checksum is derived from ordered entity hashes to detect tampering.
- **Upload target**: `IStorageService.PutAsync` writes to the `backups` container using the requested `storageProfile`. The returned `ContentHash` replaces the manifest-level checksum for end-to-end verification.

## Configuration

`BackupRestoreOptions` controls module defaults and is bound from `Koan:Backup`:

- `DefaultStorageProfile`: fallback profile when per-operation options omit `StorageProfile`.
- `DefaultBatchSize`: base streaming batch size (per entity) used by both backup and restore services.
- `WarmupEntitiesOnStartup`: triggers discovery warmup in `BackupMaintenanceService` at application boot.
- `EnableBackgroundMaintenance`: toggles recurring maintenance (discovery refresh, validation, retention logging).
- `MaintenanceInterval`: cadence for maintenance cycles; defaults to 6 hours.
- `RetentionPolicy`: high-level keep counts for daily/weekly/monthly/yearly backups and exclusion tags (currently logged as candidates).
- `MaxConcurrency`: caps parallel work in global backup/restore helpers.
- `AutoValidateBackups`: determines whether newly written backups run verification immediately after upload.
- `CompressionLevel`: forwarded to ZIP entry creation for adjusting archive size vs speed.

Per-operation options (`BackupOptions`, `GlobalBackupOptions`, `RestoreOptions`, `GlobalRestoreOptions`) manage batch sizes, inclusion/exclusion filters, concurrency, bulk options, dry run, continue-on-error, and validation toggles.

## Background maintenance

`BackupMaintenanceService` runs only when `EnableBackgroundMaintenance=true`. It:

- Optionally warms entity discovery (`WarmupEntitiesOnStartup`).
- Refreshes discovery caches and backup catalogs.
- Validates a limited number of older backups per cycle and logs issues without interrupting the loop.
- Logs cleanup candidates based on `BackupRestoreOptions.RetentionPolicy`; deletion requires additional storage hooks.
- Catches and logs exceptions to keep the hosted service resilient.

## Diagnostics & integration

- Boot report settings include default storage profile, batch size, maintenance flags, and advertised capabilities (`AutoEntityDiscovery`, `StreamingBackup`, `SchemaSnapshots`, etc.).
- In-progress backups/restores expose `BackupProgress` / `RestoreProgress` via in-memory dictionaries for quick polling (e.g., Web controllers in `Koan.Web.Backup`).
- Logging uses structured messages (entity type, duration, counts) and emits warnings for partial failures (deserialization issues, missing entity types).
- Manifests capture performance metrics (`BackupPerformanceInfo`) for later analysis of throughput and resource usage.

## Edge cases & guidance

- Large archives: operations use streaming enumerables and per-entity batching to avoid loading entire datasets into memory; ensure storage profiles can handle large uploads.
- Partial availability: if a provider lacks backup capability, discovery still records the entity, but backup attempts will surface adapter exceptions—filter via `GlobalBackupOptions.IncludeProviders` when needed.
- Schema drift: manifests store schema snapshots; use them during validation or restore planning to detect incompatible changes.
- Restore to missing types: viability testing attempts to resolve entity types by name; consider providing custom mapping or reconciling renamed entities ahead of time.
- Cancellation: marking a backup/restore as cancelled stops progress reporting but does not abort the running stream; integrate with caller-provided `CancellationToken` for hard aborts.

## Validation notes

- Source review: `StreamingBackupService`, `OptimizedRestoreService`, `BackupStorageService`, `BackupMaintenanceService`, `Initialization/KoanAutoRegistrar`, and related models as of 2025-09-29.
- DocFX strict build executed via `pwsh -File scripts/build-docs.ps1 -ConfigPath docs/api/docfx.json -LogLevel Warning -Strict`.
