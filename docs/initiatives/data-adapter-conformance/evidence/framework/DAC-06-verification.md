---
type: EVIDENCE
domain: data
title: "DAC-06 verification — Source Integration, RecordSet, and registered reads"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: framework-owned source-only activation, inspection, neutral records, registered reads, and shared Direct projection
---

# DAC-06 verification — Source Integration, RecordSet, and registered reads

## Result

PASS for the Framework-owned Source Integration boundary. `Data.Source(name)` now resolves an exact source-only
integration without creating an Entity repository. Inspection uses provider-neutral addresses, opaque source-bound
references and signed continuations, bounded pages/samples, intrinsic traits, and source-policy-projected operations.

Registered operations are immutable `Read + Records/Scalar + Buffered` plans. Duplicate declarations, parameter
drift, unknown/mismatched axes, missing or unenforced lanes, active segmentation, scalar cardinality drift, additional
result channels, timeout, and unsupported capabilities reject before unsafe dispatch or exposure. Provider dispatch
is never replayed.

`RecordSet` preserves ordered and duplicate fields, missing versus null, provider type names, the closed scalar and
nested-value algebra, deterministic `MaterializedValueV1` accounting, first-limit completion, provider partial truth,
and cancellation/disposal. Constructor/record and writable-property projection compile ordinal readers once per
target per result. Registered reads, inspection samples, Direct queries, and Direct transactions share that path;
the former dictionary-to-JSON DTO conversion is gone.

## Executable evidence

| Evidence | Result |
|---|---|
| Source Integration focused oracle | 16/16 passing: exact compact consumer journey, source-only factory, lane/effect/result/delivery, parameters, timeout/cancellation, no replay, segmentation, scalar cardinality/value limits, inspection/source binding, sampling, record algebra/accounting/completion, telemetry, and duplicate composition |
| Existing SQLite-backed Direct matrix | 5/5 passing, including typed query and typed transaction projection through the shared ordinal path |
| Combined bounded gate | 21/21 passed in 14 seconds including build |
| `Koan.sln` restore-free build | PASS in 27 seconds; zero warnings and zero errors |
| Diff hygiene | `git diff --check` PASS; repository line-ending notices only |

The broad Data Core project remains unsuitable as a deterministic gate. Its run reached the already-recorded DAC-05
legacy query-receipt failures plus unrelated AI/host fixture pollution and timed out at 180 seconds. No compatibility
relaxation was added. The existing SQLite receipt defect remains assigned to the empty-root DAC-11 replacement.

## Ownership proof

- `IDataSourceIntegrationFactory` is a separate `IAdapterFactory` specialization; it creates source mechanics, not an
  Entity repository.
- `DataSourceIntegrationService` compiles exact provider identities and caches one source integration per configured
  route inside the host.
- `DataSourceInspector` owns policy/capability checks, source/reference validation, bounded calls, continuation
  integrity, effective operation projection, and sample-shape validation.
- `RegisteredOperationExecutor` owns plan-axis validation, source effect gate, segmentation rejection, lane proof,
  bounded parameter-plan reuse, timeout classification, cardinality, no replay, and safe telemetry.
- `RecordSetMaterializer` owns the single buffered pass, shared accounting, first non-fitting-record omission,
  completion, cancellation, additional-channel failure, and reader disposal.
- `RecordProjector` owns result-lifetime compiled ordinal conversion. No adapter receives reflection or JSON mapping
  responsibility.
- Provider/family packages own only binding leaves, native identifier/shape translation, incremental neutral readers,
  exact native failure classification, resource lifetime, and actual dispatch.

## Primer-row disposition

| Rows | DAC-06 disposition |
|---|---|
| D-01–D-04 | PASS for bounded neutral inspection, signed/source-bound continuations, opaque references, policy-projected descriptors, record-only sampling, and capability-first rejection. Provider topology/identifier proofs remain adapter cases. |
| D-05–D-08 | PASS for closed algebra, order/duplicates/presence, compiled DTO conversion, exact accounting, first-limit completion, cancellation, provider partials, additional channels, and disposal. Native conversion/resource behavior remains a child proof. |
| D-09 | PASS for the constrained `IDataSourceNativeInspector` marker; the raw common adapter is not exposed. Provider-native view content remains an adapter case. |
| F-01–F-07 | PASS for immutable duplicate-rejecting catalog, exact axes, parameter plans, effect/lane proof, scalar/record results, bounds, and cardinality. Native bindings remain family/adapter proofs. |
| F-08–F-09 | PASS structurally: Source Integration performs no readiness/provisioning or artifact mutation. Missing native artifacts pass through diagnostically. Provider artifact behavior remains a child proof. |
| F-10–F-12 | PASS for uncached/unexposed execution, segmentation fail-closed, one attempt/no replay, timeout distinction, and redacted plan telemetry. Native transparent-retry claims remain child proofs. |
| C-05, H-01–H-03, P-02 | PASS for effective-read enforcement, corrective typed failures, safe telemetry, bounded host/result caches, one-pass accounting, and compiled warm projection. Provider-relative allocation/latency budgets remain certification proofs. |

SQLite and MongoDB must implement these seams directly from their empty roots. No current adapter implementation is
grandfathered or treated as a template.
