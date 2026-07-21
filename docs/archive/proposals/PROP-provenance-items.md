---
title: Provenance Item Catalog and API Enhancements
status: Draft
authors: Systems Architecture Working Group
created: 2025-10-14
sponsor: Platform Pillar
---

**Contract**

- Inputs: Module configuration defaults, discovered runtime values, sanitization rules, provenance publication mode (auto, settings, environment, launch-kit, custom).
- Outputs: Enriched provenance descriptors consumable by Admin, CLI, and console UIs; updated provenance APIs that accept strongly-typed items and emit consistent setting metadata; optional catalog utilities for project-maintained modules.
- Error Modes: Descriptor drift versus implementation, misapplied sanitization flags, modules bypassing the catalog, provenance writers receiving null or malformed items.
- Acceptance Criteria: Shared POCO catalog exists with immutable descriptors; provenance extensions accept `ProvenanceItem` + mode; at least one core module (Koan.Data.Backup) migrates to the helpers; documentation updated for contributor onboarding; legacy string-key overloads remain temporarily with analyzer guidance.

## Context

Koan registrars currently scatter configuration metadata across multiple concerns: raw key strings sit in `Constants` classes, human-readable labels live in Admin UI maps, and provenance reporting re-specifies value semantics per module. This fragmentation increases the chance of drift between diagnostics surfaces, makes console/Web tooling inconsistent, and forces adapters to duplicate sanitization rules and descriptions. As provenance usage expands into Admin, LaunchKit, CLI, and potential API surfaces, the demand for a canonical description of settings grows.

Contributors also expressed interest in richer provenance output (label, description, acceptable values) to improve the developer experience. Today each registrar must hand-author that copy, leading to uneven quality. A centralized catalog surfaced as a recurring need in architecture reviews, but any solution must remain optional to respect Koan's greenfield flexibility.

## Goals

1. Provide a central catalog of immutable provenance descriptors (`ProvenanceItem`) as plain POCOs, exposed via `static readonly` properties, without imposing DI or lifecycle coupling.
2. Allow provenance writers to accept a descriptor plus publication mode (`auto`, `settings`, `environment`, `launchkit`, etc.) and resolved value, applying sanitization and consumer hints automatically.
3. Ensure catalog adoption remains optional—modules may continue to emit raw keys—but project-maintained modules should migrate to reduce duplication.
4. Enrich Admin and LaunchKit UIs with shared labels, descriptions, acceptable-value hints, and sanitization guidance sourced from the catalog.
5. Preserve lightweight runtime behavior: descriptor access must be allocation-free after type initialization, and provenance writers should not require per-call reflection.

## Non-Goals

- Forcing third-party modules to depend on the catalog; helpers remain advisory.
- Replacing options classes or configuration binding; descriptors complement existing options.
- Providing historical provenance auditing; each descriptor captures current metadata only.

## Proposed Model

### `ProvenanceItem` POCO

Define an immutable record within a new catalog (e.g., `Koan.Core.Hosting.Bootstrap.ProvenanceItems`) with fields such as:

```csharp
public sealed record ProvenanceItem(
    string Key,
    string Label,
    string Description,
    bool MustSanitize,
    bool IsSecret,
    string? DefaultValue,
    IReadOnlyCollection<string>? AcceptableValues,
    string? DocsLink,
    IReadOnlyCollection<string>? DefaultConsumers,
    string PillarCode,
    string ModuleName);
```

Descriptors live as `public static readonly ProvenanceItem` properties grouped by module or pillar:

```csharp
public static class BackupProvenanceItems
{
    public static readonly ProvenanceItem DefaultStorageProfile = new(
        Key: "Koan:Backup:DefaultStorageProfile",
        Label: "Default Storage Profile",
        Description: "Selects the storage adapter profile used for backups when no entity policy overrides exist.",
        MustSanitize: false,
        IsSecret: false,
        DefaultValue: "default",
        AcceptableValues: Array.Empty<string>(),
        DocsLink: "docs/reference/backup/configuration.md#default-storage-profile",
        DefaultConsumers: new[]
        {
            "Koan.Data.Backup.Core.StreamingBackupService",
            "Koan.Data.Backup.Core.OptimizedRestoreService"
        },
        PillarCode: "data",
        ModuleName: "Koan.Data.Backup");
}
```

### Catalog Organization

- House descriptors in project assemblies that already centralize configuration keys (e.g., `Koan.Data.Backup.Infrastructure`).
- Group descriptors by module via nested static classes to maintain discoverability.
- Provide optional metadata like `AcceptableValues`, `DocsLink`, or `ValueHints` only when relevant; omit to keep descriptors minimal.
- Avoid static constructors—use inline initialization to keep type initialization predictable.

## Provenance API Enhancements

Augment `ProvenanceModuleExtensions` with overloads that accept descriptors:

```csharp
public static void AddSetting(
    this ProvenanceModuleWriter module,
    ProvenanceItem item,
    ProvenancePublicationMode mode,
    object? value,
    IReadOnlyCollection<string>? consumers = null,
    string? overrideSourceKey = null)
{
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(item);

    var resolvedConsumers = consumers ?? item.DefaultConsumers ?? Array.Empty<string>();
    var stringValue = value?.ToString();
    var displayValue = item.MustSanitize
        ? Redaction.DeIdentify(stringValue ?? string.Empty)
        : stringValue;

    module.SetSetting(item.Key, builder => builder
        .Value(displayValue)
        .Secret(item.IsSecret ? Redaction.DeIdentify : null)
        .Source(MapMode(mode), overrideSourceKey)
        .Consumers(resolvedConsumers.ToArray())
        .State(MapState(mode))
        .Note(item.Description));
}
```

`ProvenancePublicationMode` enumerates canonical publication modes (`Auto`, `Settings`, `Environment`, `LaunchKit`, `Discovery`, `Custom`) that map to existing `BootSettingSource` values and `ProvenanceSettingState` semantics. Existing string-based overloads remain available for a deprecation window but internally convert to a temporary descriptor for parity.

### Optional Notes & Tooling Support

- Include helper methods like `module.AddNote(ProvenanceItem item, string message, ProvenanceNoteKind kind)` for settings that produce supplemental notes.
- Provide a `ProvenanceItemCatalog.TryGet(string key, out ProvenanceItem item)` lookup so higher-level tooling (Admin UI, CLI) can hydrate metadata even when modules remain on string overloads.

## Adoption Plan

1. **Catalog scaffolding**: create catalog namespaces for core pillars (Data, Web, Admin). Seed descriptors for Koan.Data.Backup as the first adopter.
2. **API update**: add the new overloads in `ProvenanceModuleExtensions` and supporting enums, maintaining existing signatures.
3. **Module migration**: refactor Koan.Data.Backup registrar to use `BackupProvenanceItems`. Validate provenance output remains unchanged except for enriched metadata.
4. **UI consumption**: update Admin and LaunchKit surfaces to consult `ProvenanceItemCatalog` for labels and descriptions prior to rendering settings.
5. **Documentation**: add contributor guidance under `/docs/engineering/provenance.md` describing how to register new descriptors.
6. **Analyzer linting (optional)**: create a Roslyn analyzer that suggests known descriptors when a raw key matches the catalog.
7. **Broader rollout**: incrementally migrate other first-party modules (vector connectors, web features) during regular refactors.

## Impact Analysis

- **Developers**: configure provenance with fewer lines; reuse canonical descriptions; less chance of drift.
- **Diagnostics surfaces**: consistent terminology across Admin, CLI, and logs; ability to show acceptable values, docs links, and sanitization hints.
- **Runtime performance**: minimal overhead—descriptors are static data and reused across calls. `ProvenanceModuleExtensions` maintains allocation discipline.
- **Backward compatibility**: external modules using string overloads continue to function; migration path offers benefits without obligation.

## Risks & Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Descriptor drift (default value changes not reflected) | Require descriptor updates alongside option changes; add unit tests comparing defaults with options binding. |
| Overly large descriptor types | Keep record immutable and avoid storing heavyweight objects (e.g., delegates). |
| Sanitization misconfiguration | Provide standardized redaction helpers (`Redaction.ConnectionString`, etc.) and unit tests verifying sanitized output. |
| Catalog sprawl | Group descriptors by module and add linting to prevent duplicate keys. |
| Consumer mismatch | Allow callers to append extra consumers per invocation; document expectation that descriptors capture common consumers only. |

## Open Questions

1. Should descriptors include localized label/description variants, or do we rely on downstream UI translation? (Recommendation: future enhancement.)
2. Do we expose descriptor metadata via the provenance snapshot to power downstream tooling automatically? (Likely yes, as an optional `Metadata` bag.)
3. Should we auto-generate descriptors from options classes annotations to eliminate duplication? (Worth exploring with source generators.)
4. Does the catalog belong in `Koan.Core` or pillar-specific assemblies? (Initial proposal: pillar-specific to avoid bloating core dependencies.)

## Next Steps

- Prototype the catalog within Koan.Data.Backup, emit provenance through the new overloads, and validate Admin/CLI output.
- Draft contributor guidance and update onboarding templates to demonstrate descriptor creation.
- Evaluate analyzer feasibility to encourage catalog usage without hard enforcement.
- Revisit after two module migrations to confirm ergonomics before declaring the pattern framework-wide.
