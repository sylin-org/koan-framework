# ADR-XXXX — Rebooting Koan Backup Architecture

**Status**: Accepted  
**Date**: 2025-10-03  
**Authors**: Codex (Software Architect)

## Context

The original Koan backup implementation aimed for “zero configuration” by scanning every `IEntity<>` in the AppDomain and streaming data through `Data<TEntity, TKey>.AllStream`. In practice this produced fragile manifests (empty datasets, missing storage files), silent failures, and restore paths that relied on guesswork. Integration tests continue to fail: manifests report zero items, `StorageFile` is blank, and restore throws when it cannot find the expected ZIP entry.

We need to reboot the subsystem with explicit opt-in, reliable manifests, and guard rails that prevent silent data loss.

## Problems Observed

1. **Unbounded discovery** — Any `IEntity<>` is considered eligible even if no repository is configured. The backup loop catches exceptions, records `ItemCount = 0`, and continues, leaving operators unaware that nothing was exported.
2. **Invisible failures** — `EntityBackupInfo.ErrorMessage` is unused; manifests present failed entities as empty datasets and mark the overall backup as completed.
3. **Restore fragility** — Restore expects `StorageFile` to exist, so empty entries surface later as runtime errors rather than during backup.
4. **Configuration ambiguity** — There is no way for teams to declare which entities must be backed up, to require encryption, or to opt out intentionally. Documentation still promises “zero configuration,” which now conflicts with reliability goals.

## Goals

- Make backup participation explicit and auditable.
- Surface failures immediately and prevent “successful” manifests with empty data.
- Retain provider transparency and per-entity policy flex points (encryption, schema snapshots, etc.).
- Provide tooling to inventory eligible entities at startup and warn when coverage is missing.
- Lay groundwork for the existing streaming/storage refactors (naming, chunked writers, restore improvements).

## Decision

1. **Attribute-based opt-in**
   - Introduce `[EntityBackup]` for per-entity policies (e.g., `Encrypt = true`, `IncludeSchema = false`).
   - Introduce `[assembly: EntityBackupScope]` so modules can opt the entire assembly in (`BackupScope.All`) or require explicit decoration (`BackupScope.None`). Additional assembly defaults (e.g., `EncryptByDefault`) flow down to entities unless overridden.

2. **Startup inventory**
   - During application boot, `EntityDiscoveryService` gathers all `Entity<>` types via `AggregateConfigs`, applies scope + attributes, and emits an inventory:
     - Included entities + resolved policy.
     - Discoverable-but-uncovered entities (warning when opt-in is required).
   - Expose this inventory in logs, diagnostics, or a dedicated health endpoint so operators see gaps immediately.

3. **Manifest integrity**
   - `BackupEntityByReflection` records `ErrorMessage` instead of silently returning `ItemCount = 0`. If any entity fails, the manifest `Status` becomes `Failed` and the archive upload short-circuits.
   - `EntityBackupInfo` always contains a `StorageFile` when successful. Restore skips entries with `ErrorMessage` and surfaces the failure early.

4. **Restore alignment**
   - `OptimizedRestoreService` honours `ArchiveStorageKey` from the manifest and refuses to proceed when the overall manifest failed.
   - Restore reports skipped/failed entities explicitly.

5. **Future refactors retained**
   - The previously planned streaming writer, partition awareness, and manifest extensions remain in scope. Opt-in and failure signalling are prerequisites that unblock those efforts.

## Consequences

- Teams must annotate entities or assemblies intentionally; backups are no longer “magically” enabled. Missing attributes will generate warnings (and can be promoted to errors in strict environments).
- Documentation and samples must explain the attribute model, inventory output, and how to interpret warnings.
- Existing integrations relying on automatic discovery need migration guidance (add `[EntityBackup]` or assembly scope attributes).
- The implementation roadmap now has two tracks: (1) policy/manifest hardening (this ADR) and (2) streaming/storage improvements from the previous proposal.

## Implementation Plan (Incremental)

### Phase 1: Attribute Foundation

**Goal:** Establish opt-in attributes and policy resolution without breaking existing code.

1. **Create attributes** in `Koan.Data.Backup.Attributes`:
   - `[EntityBackup(Encrypt, IncludeSchema, Enabled, Reason)]` for per-entity policies
   - `[EntityBackupScope(Mode, EncryptByDefault)]` for assembly-level defaults
   - `BackupScope` enum: `None` (require explicit), `All` (auto-include)

2. **Extend `EntityDiscoveryService`**:
   - Add `BuildInventory()` method that:
     - Scans all `Entity<>` types via `AggregateConfigs`
     - Reads assembly-level `[EntityBackupScope]` attributes
     - Reads entity-level `[EntityBackup]` attributes
     - Resolves effective policy (assembly defaults + entity overrides)
     - Produces `BackupInventory` with included/excluded/uncovered lists
   - Add `ResolvePolicy(assemblyScope, entityAttr)` helper

3. **Create inventory models** in `Koan.Data.Backup.Models`:
   ```csharp
   public class BackupInventory
   {
       public List<EntityBackupPolicy> IncludedEntities { get; set; } = new();
       public List<EntityBackupPolicy> ExcludedEntities { get; set; } = new();
       public List<string> Warnings { get; set; } = new();
   }

   public class EntityBackupPolicy
   {
       public Type EntityType { get; set; } = default!;
       public bool Encrypt { get; set; }
       public bool IncludeSchema { get; set; } = true;
       public string Source { get; set; } = ""; // "Assembly" or "Attribute"
       public string? Reason { get; set; } // For opt-out justification
   }
   ```

4. **Startup validation**:
   - Wire `EntityDiscoveryService.BuildInventory()` into `KoanAutoRegistrar`
   - Log inventory results during boot (see Boot Report Integration in BACKUP-SYSTEM.md)
   - Emit warnings for uncovered entities in `BackupScope.None` assemblies

### Phase 2: Manifest Integrity

**Goal:** Ensure failures are explicit and restore operations are safe.

1. **Update `StreamingBackupService`**:
   - Change backup loop to capture exceptions in `EntityBackupInfo.ErrorMessage`
   - Mark `BackupManifest.Status = Failed` if any entity fails
   - Ensure `EntityBackupInfo.ItemCount > 0` and `StorageFile != null` on success
   - Include policy metadata in `EntityBackupInfo` (encrypt, includeSchema)

2. **Update `EntityBackupInfo` model**:
   ```csharp
   public class EntityBackupInfo
   {
       // ... existing fields ...
       public string? ErrorMessage { get; set; } // NEW: Capture failure reason
       public bool Encrypt { get; set; } // NEW: Policy metadata
       public bool IncludeSchema { get; set; } = true; // NEW: Policy metadata
   }
   ```

3. **Update `OptimizedRestoreService`**:
   - Check `BackupManifest.Status != Failed` before proceeding
   - Skip entities with `ErrorMessage != null`, log warning
   - Fail restore if overall manifest is marked `Failed`
   - Use `ArchiveStorageKey` from manifest, fallback to legacy naming

4. **Strengthen integration tests**:
   - Assert `ItemCount > 0` for all included entities
   - Assert `ErrorMessage == null` for successful backups
   - Assert `Status = Failed` when entity streaming throws
   - Test restore rejection of failed manifests

### Phase 3: Policy Enforcement

**Goal:** Apply encryption and schema policies during backup/restore.

1. **Implement encryption support**:
   - Add `IBackupEncryptor` interface
   - Wire encryption into `StreamingBackupService` when `policy.Encrypt = true`
   - Update restore to decrypt when manifest indicates encryption
   - Document encryption key management (future: integration with Koan.Security)

2. **Implement schema inclusion**:
   - When `policy.IncludeSchema = false`, skip schema snapshot in backup
   - Update restore to handle missing schema (assume current schema)

3. **Validation and diagnostics**:
   - Add `/backup/inventory` endpoint to expose `BackupInventory`
   - Add health check for uncovered entities
   - Consider Roslyn analyzer for build-time warnings (future)

### Phase 4: Migration and Documentation

**Goal:** Help teams migrate from auto-discovery to attribute-based opt-in.

1. **Migration guide**:
   - Document how to add `[assembly: EntityBackupScope(Mode = BackupScope.All)]` for backward compatibility
   - Provide checklist for teams to review entity coverage
   - Explain when to use `BackupScope.None` (strict mode)

2. **Update samples and tests**:
   - Decorate all sample entities with `[EntityBackup]`
   - Update integration tests to use attributes
   - Provide examples of encryption and schema policies

3. **Boot report enhancement**:
   - Ensure inventory output is clear and actionable
   - Provide guidance on resolving warnings

## Design Details

### Attribute Examples

```csharp
using Koan.Data.Backup.Attributes;

// Basic opt-in
[EntityBackup]
public class Media : Entity<Media> { }

// PII encryption
[EntityBackup(Encrypt = true)]
public class User : Entity<User> { }

// Skip schema to reduce size
[EntityBackup(IncludeSchema = false)]
public class LogEntry : Entity<LogEntry> { }

// Explicit opt-out with justification
[EntityBackup(Enabled = false, Reason = "Derived view, rebuild from source")]
public class SearchIndex : Entity<SearchIndex> { }
```

### Assembly Scope Examples

```csharp
// Opt-in all entities by default
[assembly: EntityBackupScope(Mode = BackupScope.All)]

// Opt-in all with encryption by default
[assembly: EntityBackupScope(Mode = BackupScope.All, EncryptByDefault = true)]

// Require explicit decoration (strict mode)
[assembly: EntityBackupScope(Mode = BackupScope.None)]
```

### Policy Resolution Rules

1. **No assembly scope + No entity attribute** → Warning (not backed up)
2. **`BackupScope.All` + No entity attribute** → Included (inherit assembly defaults)
3. **`BackupScope.All` + `[EntityBackup]`** → Included (entity overrides assembly)
4. **`BackupScope.None` + No entity attribute** → Warning (not backed up)
5. **`BackupScope.None` + `[EntityBackup]`** → Included (explicit opt-in)
6. **Any scope + `[EntityBackup(Enabled = false)]`** → Excluded (explicit opt-out, no warning if `Reason` provided)

### Inventory Output Contract

```csharp
public class BackupInventory
{
    // Entities included in backup (via scope or attribute)
    public List<EntityBackupPolicy> IncludedEntities { get; set; }

    // Entities explicitly excluded (Enabled = false)
    public List<EntityBackupPolicy> ExcludedEntities { get; set; }

    // Entities without coverage that should warn
    public List<string> Warnings { get; set; }
}
```

**Boot log format:**

```
[INFO] Koan:backup   included: N entities
[INFO] Koan:backup     <EntityName> → encrypt=<bool>, schema=<bool> (via <source>)
[WARN] Koan:backup   uncovered: N entities
[WARN] Koan:backup     <EntityName> → no backup coverage (assembly scope: <mode>)
[INFO] Koan:backup   excluded: N entities (explicit opt-out)
[INFO] Koan:backup     <EntityName> → reason: "<reason>"
```

## Open Questions

### Resolved
- **Analyzers?** Yes, add to Phase 3 roadmap (build-time warnings via Roslyn)
- **Opt-out UX?** Use `[EntityBackup(Enabled = false, Reason = "...")]` - treated as success (no warning) if reason provided
- **Module-level defaults?** Use `[assembly: EntityBackupScope(EncryptByDefault = true)]` for simple cases; future ADR for complex retention policies

### Remaining
- **Encryption key management:** How do we integrate with Koan.Security for key rotation and storage? (Future ADR)
- **Partition-aware backup:** Should `[EntityBackup]` support partition filters? (Future enhancement)
- **Incremental backup:** How do we track "last backup" per entity for incremental mode? (Future ADR)
