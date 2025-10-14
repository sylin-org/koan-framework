# ARCH-0066 Boot report hub and handles

**Contract**

- **Inputs:** Boot report generation in `Koan.Core`, auto-registrar `Describe` methods, `AdapterConnectionDiagnostics<T>` snapshots, boot report augmenters resolved via DI, configuration readers that emit secrets or connection URIs.
- **Outputs:** Static `BootReportHub` staging surface, `BootModuleHandle` authoring helpers, updated `BootReport` ingestion path, hub-backed adapter reporting utilities, and updated documentation for module authors.
- **Error Modes:** Missed hub commits leading to empty reports, duplicate module entries when mixing legacy APIs, race conditions while staging settings, regressions in secret redaction, or loss of setting keys needed by the admin UI.
- **Success Criteria:** Startup diagnostics no longer block on DI augmenters, module notes/settings populate through hub handles, keys remain intact for UI correlation, secrets are consistently sanitized, and legacy callers continue to function until migrated.

**Edge Cases**

1. Multiple modules updating the same setting concurrently.
2. Boot report requested before hub commit (e.g., early logging).
3. Adapters emitting connection strings with embedded credentials.
4. Legacy `BootReport` consumers mixing direct mutations with hub usage.
5. Modules omitting version/description but still reporting settings.

## Context

Boot reporting currently relies on `IBootReportAugmenter` instances resolved from DI after the host container builds. Each augmenter receives a live `BootReport`, performs discovery, and mutates the in-memory builders directly. This pattern introduces several issues:

- **Late binding:** Modules have to wait until dependency injection is available, so long-running discovery (e.g., Mongo health checks) blocks bootstrap instrumentation.
- **Tight coupling:** Augmenters have to pull diagnostics services through the container even when data already exists.
- **Volatile sequencing:** Notes and settings frequently race with `Describe` outputs because augmenters execute after initial report construction.
- **DX friction:** Developers cannot trivially author boot snippets without understanding the augmenters + DI pattern.

The goal is to make boot report authoring cheap and deterministic. A static staging hub lets any component (config readers, diagnostics, background services) publish insights immediately. When the host is ready to surface the report, the hub materializes every staged module into the `BootReport`. Keys and sanitized values must still feed downstream surfaces like Koan Admin.

## Decision

Introduce a static `BootReportHub` that records module data through lightweight `BootModuleHandle` structures. The hub stores:

- Module identity (name, version, description, status).
- Settings with preserved keys, sanitized display values, `BootSettingScope`, source metadata, and consumer hints.
- Notes and tools appended at will.

`BootReportHub.CommitTo(BootReport)` merges staged modules into the runtime report and resets internal state. `BootReport` gains merge helpers so hub data can coexist with legacy direct mutations during the migration window.

Redaction helpers (`BootReportRedactors`) provide canonical sanitizers (connection strings, API keys, tokens) to keep literals centralized.

## Rationale

- **Deterministic bootstrap:** reporting happens at the moment insights are discovered, without waiting for DI scopes.
- **Simpler authoring:** developers can call `BootReportHub.Module("data:mongo")` from any context and immediately set notes/settings.
- **UI alignment:** explicit setting keys survive the sanitization pipeline so the admin UI and other reporters can map values accurately.
- **Isolation:** secrets are sanitized consistently by reusable helpers rather than ad-hoc string manipulation.
- **Gradual migration:** `BootReport` still exposes legacy methods, so existing registrars keep working while auto-registrars/diagnostics adopt handles over time.

## Implementation Plan

1. Add `BootReportHub`, `BootModuleHandle`, staging records, and `BootSettingScope` enum in `Koan.Core.Hosting.Bootstrap`.
2. Extend `BootReport` with hub merge helpers, scope-aware setting entries, and small refactors to support sanitized display values.
3. Provide `BootReportRedactors` for canonical sanitization and update adapter diagnostics to reuse them.
4. Update runtime bootstrap (`AppRuntime`) to commit hub state before generating console blocks.
5. Migrate framework helpers (`AdapterBootReporting`, diagnostics) to stage data through the hub; remove DI augmenters in subsequent steps.
6. Refresh documentation and samples to highlight the new pattern, then track removal of legacy augmenters as a follow-on task.

## Consequences

- Modules can push boot telemetry as soon as it is known, reducing perceived startup hangs.
- Boot report output remains stable even if some components still touch `BootReport` directly; the merge logic arbitrates duplicates.
- Secret handling becomes consistent via dedicated redactors.
- Additional runtime tests will be required to ensure hub commits happen exactly once per host lifecycle.
- Follow-up cleanup is needed to drop `IBootReportAugmenter` registrations once consumers adopt the hub fully.

## Status

Accepted – implementation in progress (2025-10-14).
