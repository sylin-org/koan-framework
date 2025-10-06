# Proposal: Canon Aggregation Contract & Policies

**Status**: Draft  
**Date**: 2025-10-05  
**Authors**: Framework Architecture & Data Working Group  
**Related**: ARCH-0058 (Canon Runtime Architecture), DATA-0061 (Pagination & Streaming)

---

## Executive Summary

Canon adoption stalls when teams must hand-roll aggregation, conflict resolution, and auditing. This proposal introduces a minimal, declarative contract for canonical models:

- Every `CanonEntity<T>` must expose at least one `[AggregationKey]` property and can optionally annotate additional fields with the same attribute for composite keys.
- Field-level merge behavior is declared with `[AggregationPolicy(...)]`, supporting `First`, `Latest`, `Min`, and `Max`, with `Latest` resolved by arrival order (GUID v7 IDs or pipeline timestamps).
- `[Canon(Audit: true)]` lets projects opt specific canon models into enhanced auditing without custom plumbing.

The runtime discovers these attributes at startup, wires the default aggregation contributor, records property-level provenance, and keeps pipelines override-friendly. Developers retain the one-liner DX (`entity.Canonize("source")`) while gaining consistent multi-source merges, deterministic policies, and turnkey auditing.

---

## Problem Statement

### DX & Implementation Gaps

- Aggregation requires bespoke contributors per model; no declarative way to mark aggregation keys.
- Merge semantics (last write wins, minima/maxima) live in handwritten code, increasing drift and test burden.
- Opting into auditing demands manual observers, persistence, and documentation updates.

### Data & Governance Gaps

- Multi-source merges are error-prone; canonical entities often duplicate or lose external IDs.
- Lack of consistent policy metadata limits lineage, reconciliation, and analytics.
- Auditing is uneven, undermining compliance posture for regulated domains.

---

## Proposal Details

### Minimal Canon Contract

- **Model Opt-in**: `public sealed class Device : CanonEntity<Device> { ... }`.
- **Aggregation**: Annotate one or more properties with `[AggregationKey]`. The runtime composes composite keys by property order.
- **Invocation**: `await device.Canonize("crm")` remains the primary DX—no transport scaffolding required.
- **Validation**: Startup throws or logs a high-severity warning when a `CanonEntity<T>` lacks `[AggregationKey]`.

### Aggregation Pipeline Enhancements

1. **Metadata Discovery**
   - `CanonRuntimeBuilder` scans assemblies for `[AggregationKey]`, `[AggregationPolicy]`, and `[Canon]`.
   - Captured metadata flows into `CanonModelDescriptor` & `CanonRuntimeConfiguration`.
2. **Default Aggregation Contributor**
   - Computes composite keys, queries `CanonIndex`, and attaches/merges canonical IDs.
   - Supports deterministic conflict handling (first-in wins with healing enqueue) and reuses stored canonical metadata.
3. **Arrival Tokens**
   - Prefer GUID v7 IDs to derive timestamps; fall back to `CanonizationEvent.OccurredAt` or explicit pipeline-supplied arrival ticks.
   - Store per-property arrival metadata in `CanonMetadata.PropertyFootprints` for policy evaluation and audit.

### Aggregation Policies

- **Attribute**: `[AggregationPolicy(AggregationPolicyKind.Latest)]` on properties requiring managed merges.
- **Default**: Properties without an explicit attribute implicitly use `Latest`, retaining prior behavior while allowing opt-in overrides.
- **Policies**:
  - `First`: retain initial non-null value.
  - `Latest`: prefer newest arrival using GUID v7 or arrival timestamp.
  - `Min` / `Max`: evaluate numerics, `DateTime`, `DateTimeOffset`, or `string` (lexicographic). Unsupported types log warnings and skip policy.
- **Execution Order**: Policies run after aggregation, before custom pipeline contributors. Contributors can override results intentionally.
- **Telemetry**: Policy decisions (previous value, incoming value, winner, policy, arrival token) captured in structured logs and optional audit sinks.

### Auditing Toggle

- **Attribute**: `[Canon(Audit: true)]` placed on the canonical type.
- **Behavior**:
  - Persist detailed change records (before/after snapshots, policy decisions) alongside `CanonizationRecord`.
  - Wire default audit sinks (e.g., `CanonAuditLog` entity or telemetry pipeline) and expose availability via diagnostics endpoints.
  - Allow retention/config overrides through `CanonOptions` or environment configuration.

### Developer Experience Summary

```csharp
[Canon(Audit: true)]
public sealed class Device : CanonEntity<Device>
{
    [AggregationKey]
    public string SerialNumber { get; set; } = string.Empty;

    [AggregationKey]
    public string? Manufacturer { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? DisplayName { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Min)]
    public DateTimeOffset FirstSeen { get; set; }
}

var device = new Device
{
    SerialNumber = "SN-123",
    Manufacturer = "MegaCorp",
    DisplayName = "Device A",
    FirstSeen = DateTimeOffset.UtcNow
};

await device.Canonize("source-crm");
```

- `.Canonize()` merges multi-source payloads automatically.
- `.SendForCanonization()` / staging paths remain available for transport-driven apps.
- Observers and policies are centrally discoverable.

---

## Architecture & Data Implications

- **Runtime Metadata**: Extend `CanonModelDescriptor` with `AggregationKeys`, `AggregationPolicies`, and `AuditEnabled` flags.
- **CanonIndex**: Support multiple aggregation entries per canonical ID; index entries include arrival token and source metadata.
- **CanonMetadata**: Add `PropertyFootprints` capturing `{ property, lastValue, sourceKey, arrivalToken }`.
- **Observers & Telemetry**: Update default observer to include policy outcomes, policy kind, and audit status.
- **Controllers**: `CanonModelsController` exposes aggregation keys, policies, and audit flag for tooling.

---

## Implementation Plan

1. **Attribute & Metadata Layer**
   - Introduce `[AggregationKey]`, `[AggregationPolicy]`, `[Canon]`, and policy enum in `Koan.Canon.Domain`.
   - Extend metadata scanning and descriptors to capture attribute data.
2. **Runtime Engine Updates**
   - Add default aggregation contributor honoring aggregation keys and policies.
   - Implement policy strategy registry handling `First`, `Latest`, `Min`, `Max` with arrival tokens.
   - Enhance `CanonMetadata` + `CanonIndex` with property footprint support and deterministic conflict handling.
3. **Auditing Pipeline**
   - Wire audit toggle into `CanonRuntime`; produce enriched `CanonizationRecord` + audit sink when enabled.
   - Provide default audit persistence and configuration knobs (retention, sink selection).
4. **DX & Documentation**
   - Update docs (`docs/reference/canon/index.md`, samples) to demonstrate annotation usage.
   - Add unit tests for policy behavior, composite keys, and auditing toggles (e.g., `CanonRuntimeTests` scenarios).
5. **Operational Tooling**
   - Surface aggregation & audit metadata via controllers/CLI.
   - Establish monitoring dashboards using policy decision telemetry.

---

## Current Implementation Delta

- **Annotations**: No runtime attributes exist today for aggregation keys, policies, or audit toggles. Models rely on bespoke contributors instead of declarative metadata.
- **Runtime Metadata**: `CanonRuntimeBuilder`, `CanonPipelineMetadata`, and `CanonRuntimeConfiguration` expose only pipeline phases. Aggregation keys, policy maps, audit flags, arrival tokens, and property footprints are not yet discovered or stored.
- **Default Contributors**: The aggregation merge logic in tests/samples is handcrafted. There is no built-in contributor for composite key resolution, deterministic policy evaluation, or canonical index maintenance.
- **CanonIndex & Metadata**: `CanonIndex` lacks arrival tokens and multi-owner support; `CanonMetadata` doesnt track per-property provenance or structured policy outcomes beyond existing snapshots.
- **Auditing Surface**: No dedicated audit sink/entity exists. Observers capture events, but audit trails, retention knobs, and diagnostics endpoints remain unimplemented.
- **Tooling & Docs**: `CanonModelDescriptor`/web controllers dont expose aggregation metadata, and samples/documentation still demonstrate manual pipelines.

---

## Next Steps

1. **Introduce Attribute Layer**: Add `[AggregationKey]`, `[AggregationPolicy]`, `[Canon]`, and `AggregationPolicyKind`, including validation and default (`Latest`) semantics.
2. **Metadata Discovery & Configuration**: Extend runtime startup to scan assemblies, populate enriched descriptors (keys, policies, audit flags), and surface them through configuration and web catalog APIs.
3. **Default Runtime Contributors**: Implement built-in aggregation/policy contributors with arrival token handling, policy registry, and audit hooks; refactor samples/tests to exercise the new path.
4. **Persistence Enhancements**: Update `CanonIndex`, `CanonMetadata`, and introduce an audit log sink/entity to record property footprints, policy decisions, and retention controls.
5. **Documentation & DX**: Refresh guides, reference docs, and samples to showcase the declarative annotations, plus add operational guidance for audit configuration and monitoring.

---

## Risks & Mitigations

- **Type Support for Min/Max**: Limit to well-known types; document extension hook for custom comparers.
- **Arrival Token Consistency**: Standardize GUID v7 generation for staging and transport layers; fallback timestamps recorded in UTC.
- **Performance Overhead**: Policy evaluation adds minor cost; leverage caching and guard logs to INFO level only when auditing is enabled.
- **Backward Compatibility**: Provide migration scripts to populate aggregation attributes in existing models; allow transitional mode with warnings before enforcing hard failures.

---

## Open Questions

- Should `AggregationPolicy` accept custom strategy types for domain-specific merges?
- Do we require per-property audit suppression even when `[Canon(Audit: true)]` is set?
- How should we surface healing workflows when aggregation conflicts cannot be auto-resolved by policy?
- What retention defaults align with compliance needs across industries (healthcare vs. retail)?
