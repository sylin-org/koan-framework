---
uid: reference.modules.koan.data.backup
title: Koan.Data.Backup – Technical Reference
description: Streaming backup and restore services with discovery, storage integration, and maintenance workflows for Koan entities.
since: 0.6.3
packages: [Sylin.Koan.Data.Backup]
source: src/Koan.Data.Backup/
validation:
  date: 2026-07-14
  status: source-reviewed
---

## Contract

- Stream entity enumeration during backup and restore through Koan entity/repository surfaces. The
  current ZIP archive is still assembled in memory before upload.
- Auto-register backup, restore, discovery, and optional maintenance services when the package is referenced.
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

1. **Reference and policy declaration** – The package auto-registrar calls `AddKoanBackupRestore()`.
   `[EntityBackup]` and assembly-level `[EntityBackupScope]` control discovery participation.
2. **Entity discovery** – `EntityDiscoveryService` scans Koan aggregates, caching `EntityTypeInfo` records (entity type, key type, provider). Its pre-scan fallback consumes Data Core's provider-free registered-type facts and resolves provider metadata against the service's injected host; it does not inspect Data Core private caches or inherit a prior host's repository. Optional warmup occurs during maintenance startup when `WarmupEntitiesOnStartup` is enabled.
3. **Streaming export** – `StreamingBackupService` orchestrates backups:
   - Creates a manifest and progress record (stored in `_activeBackups`).
   - Builds a ZIP archive in memory via `BackupStorageService.CreateBackupArchive`.
   - Streams each entity using `Data<TEntity, TKey>.AllStream(batchSize)` (single-entity) or reflection-based enumeration for global backups.
   - Serializes records as JSON Lines and collects schema metadata, sizes, checksums, and timing metrics per entity.
   - After all entities are processed, writes manifest & verification files (`manifest.json`, `verification/*`) and uploads the archive using `IStorageService` under the configured storage profile.
4. **Progress & cancellation** – `GetBackupProgress` returns process-local aggregate metrics, while
   `CancelBackup` marks the process-local record as `Cancelled`. Use the caller cancellation token to
   stop active I/O. Backups run sequentially inside a single archive to avoid ZIP concurrency issues.

## Restore workflow

1. **Manifest loading** – `OptimizedRestoreService` locates backup archives through `BackupStorageService` and loads the stored `BackupManifest`.
2. **Adapter preparation** – If the entity repository implements `IRestoreOptimizedRepository`, `PrepareForRestoreAsync` receives `RestorePreparationOptions` (estimated counts, flags for disabling constraints/indexes, bulk mode). The returned context is restored after the operation.
3. **Data import** – Entities are read from stored JSON Lines via `BackupStorageService.ReadEntityDataAsync<T>` or reflection fallback (`ReadEntityDataAsObjects`). Records are upserted using `Data<TEntity, TKey>.UpsertManyAsync` in batches, respecting dry-run and continue-on-error settings.
4. **Progress & viability** – Global restores manage concurrent operations with a configurable
   `SemaphoreSlim`. `TestRestoreViability` inspects manifests, checks entity types, and estimates
   restore time, capturing adapter optimization availability in a report.
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

- Backup deletion: managed deletion is not implemented. `EntityBackupExtensions.DeleteBackup(...)`
  returns a faulted task with `NotSupportedException`, explicitly states that nothing was deleted, and
  does not resolve services or touch storage. Retain the archive until a verified backup-management
  operation and deletion receipt contract exist.
- Large archives: entity iteration is streamed and batched, but `BackupStorageService` currently holds
  the complete compressed archive in a `MemoryStream` until upload. Large-backup memory safety is not
  established.
- Encryption: `EntityBackupAttribute.Encrypt` is recorded as policy/manifest metadata; the archive
  writer currently performs no payload encryption.
- Partial availability: if a provider lacks backup capability, discovery still records the entity, but backup attempts will surface adapter exceptions—filter via `GlobalBackupOptions.IncludeProviders` when needed.
- Schema drift: manifests store schema snapshots; use them during validation or restore planning to detect incompatible changes.
- Restore to missing types: viability testing attempts to resolve entity types by name; consider providing custom mapping or reconciling renamed entities ahead of time.
- Cancellation: marking a backup/restore as cancelled stops progress reporting but does not abort the running stream; integrate with caller-provided `CancellationToken` for hard aborts.

## Validation notes

- Safety proof: `Koan.Data.Backup.Tests` pins fail-loud deletion behavior and its non-success message.
- Host-ownership proof: registered-type fallback discovery resolves provider metadata against an
  explicitly supplied host through the supported Data Core inspection surface.
- Source review: `StreamingBackupService`, `OptimizedRestoreService`, `BackupStorageService`,
  `BackupMaintenanceService`, `Initialization/KoanAutoRegistrar`, and related models as of 2026-07-14.
- Maturity boundary: no end-to-end storage-plus-data-adapter backup/restore conformance suite currently
  supports production recovery claims.
- DocFX strict build executed via `pwsh -File scripts/build-docs.ps1 -ConfigPath docs/api/docfx.json -LogLevel Warning -Strict`.
