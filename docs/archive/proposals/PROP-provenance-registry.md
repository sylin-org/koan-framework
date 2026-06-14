---
title: Koan Provenance Registry
status: Draft
authors: Systems Architecture Working Group
created: 2025-10-14
sponsor: Platform Pillar
---

**Contract**

- Inputs: Pillar catalog descriptors, module registrations, provenance `SetSetting`/`SetTool`/`SetNote` calls, asynchronous discovery results, Koan environment context.
- Outputs: Immutable provenance snapshot published via `KoanEnv.Provenance`, structured diagnostics surface for Admin, LaunchKit, and observability exporters, bootstrap log serialization derived from the same snapshot.
- Error Modes: Duplicate module identifiers inside a pillar, conflicting note keys, stalled snapshot publishing (registry write contention), consumers caching stale references without checking `Version` stamps.
- Acceptance Criteria: Registry supports incremental updates after boot; BootReport/BootReportHub removed in favor of registry-backed snapshot; Admin manifests consume the snapshot; sample adapters (e.g., Mongo) demonstrate runtime updates; documentation/ADRs updated to reference the new model.

## Context

Koan currently gathers configuration provenance during bootstrap by instantiating every `IKoanAutoRegistrar` and populating a transient `BootReport`. Consumers—bootstrap logging, Admin manifest generation, LaunchKit—replay the same reflection-heavy flow each time they need data. This design assumes configuration is fully resolved at boot, blocking asynchronous discovery and adding significant complexity to adapters that stage connections.

WEB-0061 and WEB-0062 raised the bar for admin diagnostics by requiring real-time module detail and pillar visualization. The existing provenance path cannot deliver incremental updates or a canonical runtime snapshot. Mongo and vector connectors, for example, perform warmup after boot and lack a sanctioned path to publish discovered metadata. We need a lean, runtime-friendly registry that becomes the single source of truth for configuration facts.

## Goals

1. Provide a process-wide provenance registry where pillars register modules and modules set settings, tools, and notes at any time.
2. Expose an immutable, always-current snapshot under `KoanEnv.Provenance` so diagnostics and tooling read without service lookup.
3. Support asynchronous updates from adapters (e.g., discovery, health probes) without replaying registrars.
4. Remove the legacy BootReport implementation and its helper APIs to avoid dual sources of truth.
5. Keep reporting semantics (sources, redaction, consumers, state) explicit and auditable.
6. Preserve lightweight boot logging by formatting the new snapshot instead of maintaining bespoke report builders.

## Non-Goals

- Reimplement pillar catalog logic (WEB-0062); the registry will consume existing descriptors.
- Produce streaming telemetry; consumers should poll or diff snapshots.
- Introduce persistence; provenance remains in-memory.
- Modify launch policy or module authorization—only the data publication path changes.

## Proposed Architecture

### Core Types

| Type                 | Responsibility                                                                                                             |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `ProvenanceRegistry` | Central mutable store; manages pillars, modules, settings, tools, notes; enforces keys and publishes snapshots.            |
| `ProvenancePillar`   | Aggregates modules under a pillar code; carries label, color, icon pulled from `KoanPillarCatalog`.                        |
| `ProvenanceModule`   | Represents a module inside a pillar (name, version, description, status, timestamps). Provides setters for module content. |
| `ProvenanceSetting`  | Keyed configuration fact (`key`, `value`, `isSecret`, `source`, `sourceKey`, `consumers[]`, `state`, `updatedUtc`).        |
| `ProvenanceTool`     | Action descriptor (`name`, `route`, `description`, `capability`).                                                          |
| `ProvenanceNote`     | Keyed note (`key`, `message`, `kind`, `updatedUtc`).                                                                       |
| `ProvenanceSnapshot` | Immutable graph of pillars → modules → settings/tools/notes with a `Version` and `CapturedUtc`.                            |

### Registry Semantics

- Writes occur through handles returned by `ProvenanceRegistry.GetOrCreateModule(pillarCode, moduleName)`.
- Handle methods:
  - `SetSetting(string key, Action<ProvenanceSettingBuilder> configure)`
  - `RemoveSetting(string key)` (optional for cleanup)
  - `SetTool(string name, Action<ProvenanceToolBuilder> configure)`
  - `RemoveTool(string name)`
  - `SetNote(string key, Action<ProvenanceNoteBuilder> configure)`
  - `RemoveNote(string key)`
  - `SetStatus(string status, string? detail = null)`
- Builders capture optional source metadata, redaction delegates, state (`Configured`, `Discovered`, `Default`, `Error`), and consumer identifiers.
- Registry ensures operations are idempotent; repeated `Set*` calls update timestamp and overwrite prior values.
- Modules can call setters from async continuations (e.g., readiness callbacks) without re-registering.

### Snapshot Publication

- Registry maintains an internal version counter. Each mutation produces a new immutable snapshot object.
- Snapshot is stored at `KoanEnv.Provenance` and exposed via `IProvenanceSnapshotProvider.TakeSnapshot()`.
- Updates use copy-on-write to avoid blocking readers: the registry clones affected module trees, applies changes, increments version, then atomically swaps the snapshot reference.
- Consumers should read `KoanEnv.Provenance` (null-safe) and may compare `Version` values to detect changes.
- Optionally emit an event (`ProvenanceRegistry.SnapshotUpdated`) for observers (e.g., diagnostics) who prefer push models.

### Pillar Integration

- Registry seeds pillar metadata from `KoanPillarCatalog`. Unknown modules default to the catalog’s fallback descriptor.
- Pillars live indefinitely; modules can be removed explicitly or left as-is until process restarts.
- Pillar descriptors remain the authority for label/color/icon to keep WEB-0062 alignment.

### Bootstrap Logging

- On `KoanEnv` initialization, after registrars register known defaults, the runtime obtains `KoanEnv.Provenance` and formats it for console/log output. Existing `BootReportOptions` filters (show decisions, compact) migrate to snapshot formatters.
- Legacy classes (`BootReport`, `BootReportHub`, `BootReportRedactors`, adapter reporting helpers) are removed once consumers adopt the registry.

## Reference Usage

### Module Registration at Boot

```csharp
public sealed class MongoConnectorRegistrar : IKoanAutoRegistrar
{
    public void Describe(IProvenanceRegistry provenance, IConfiguration cfg, IHostEnvironment env)
    {
        var module = provenance
            .GetOrCreateModule("data", "Koan.Data.Connector.Mongo")
            .Describe(version: typeof(MongoConnectorRegistrar).Assembly.GetName().Version?.ToString());

        module.SetSetting("connection.string", setting => setting
            .Value(cfg["Koan:Data:Mongo:ConnectionString"])
            .Secret(redactor: Redaction.ConnectionString)
            .Source(BootSettingSource.AppSettings, "Koan:Data:Mongo:ConnectionString")
            .Consumers("Koan.MongoDataAdapter")
            .State(ProvenanceSettingState.Configured));

        module.SetStatus("warming-up");
        module.SetNote("warmup", note => note.Message("Mongo warmup scheduled asynchronously."));
    }
}
```

### Asynchronous Discovery Update

```csharp
private void ReportWarmupReady(MongoOptions options)
{
    var module = ProvenanceRegistry.Current
        .GetOrCreateModule("data", "Koan.Data.Connector.Mongo");

    module.SetStatus("ready");

    module.SetSetting("connection.string", setting => setting
        .Value(options.ConnectionString)
        .Secret(Redaction.ConnectionString)
        .Source(MapSourceFromDiagnostics())
        .Consumers("Koan.MongoDataAdapter")
        .State(ProvenanceSettingState.Discovered));

    module.SetNote("warmup", note => note
        .Message("Mongo connection established asynchronously.")
        .Kind(ProvenanceNoteKind.Info));
}
```

Both boot-time and runtime code paths operate on the same module handle; each call refreshes `KoanEnv.Provenance` automatically.

## Migration & Removal Strategy

1. **Introduce registry**: implement `ProvenanceRegistry`, handle types, builders, and snapshot provider; wire initialization inside `KoanEnv.TryInitialize`.
2. **Expose ambient snapshot**: add `KoanEnv.Provenance` property (nullable) with thread-safe updates.
3. **Update registrars**: refactor `IKoanAutoRegistrar.Describe` signature to accept `IProvenanceRegistry` (with compatibility shim for staged rollout). Provide helper extension methods for common patterns (e.g., `configuration.ReadWithSource` replacement).
4. **Port consumers**:
   - `KoanAdminManifestService` reads `KoanEnv.Provenance` instead of replaying registrars.
   - Admin SPA uses pillar/module hierarchy from snapshot.
   - LaunchKit, telemetry exporters, CLI bootstrap commands consume the snapshot.
5. **Replace boot logging**: build new formatter (e.g., `ProvenanceFormatter`) to serialize the snapshot using existing console style options.
6. **Remove legacy paths**: delete `BootReport`, `BootReportHub`, `BootReportRedactors`, and adapter helper classes (`AdapterBootReporting`, etc.), along with related unit tests and documentation referencing them.
7. **Documentation update**: refresh WEB-0061/WEB-0062 references, add ADR capturing the change, update developer guides to reference `ProvenanceRegistry`.

Because Koan is greenfield, we will remove deprecated code in the same release once registry adoption lands; no dual path will persist.

## Impacted Components

- **Adapters**: connectors update to call `SetSetting`/`SetNote` instead of `BootReportHub`. The Mongo warmup flow becomes a primary example.
- **Core Hosting**: `AppRuntime` consumes the snapshot for logging; `IKoanAutoRegistrar` signature change propagates through framework and samples.
- **Admin**: manifest and SPA simplify by reading `KoanEnv.Provenance` (no reflection). Pillar visualization reuses snapshot metadata directly.
- **LaunchKit**: exports pull configuration keys from snapshot rather than reconstructing configuration reads.
- **Observability**: readiness diagnostics can attach extra metadata via module notes without reimplementing reporting hooks.

## Validation Plan

- Unit tests for registry concurrency (simulated concurrent updates from multiple modules).
- Integration tests covering async updates (e.g., Mongo warmup) to ensure `KoanEnv.Provenance` reflects final state.
- Admin end-to-end test verifying pillar counts/settings render correctly after runtime updates.
- Bootstrap logging comparison ensuring snapshot formatter reproduces key information (modules, settings, decisions).

## Risks & Mitigations

- **Write Contention**: High-frequency updates could churn snapshots. Mitigation: coalesce updates with diff detection and reuse unchanged module structures.
- **Consumer Drift**: If consumers cache snapshots indefinitely, they miss updates. Mitigation: document `Version` usage; optionally add change notifications.
- **Migration Complexity**: Hundreds of registrars must update signatures. Mitigation: provide temporary adapter bridging extension (`ProvenanceRegistryAdapter`) to ease refactors while systematically replacing references.
- **Redaction Regressions**: New builders must respect secret handling. Mitigation: centralize standard redactors (connection strings, API keys) and add unit tests.

## Open Questions

1. Do we expose per-setting history for audit? (Out of scope for initial pass; snapshots only.)
2. Should we support module removal when adapters unload? (Default to `Remove*` API; evaluate later.)
3. Do console/CLI surfaces require diff output between snapshots? (Future enhancement.)

## References

- WEB-0061 – Koan Admin surface refresh
- WEB-0062 – Koan Admin pillar visualization
- ARCH-0044 – Standardized module config and discovery
- Existing BootReport implementation (to be removed)
