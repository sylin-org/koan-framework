---
type: SPEC
domain: data
title: "DAC-06 Implement Source Integration, RecordSet, and Registered Operations"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: Source Integration, neutral result, inspection, and registered-read implementation
---

# DAC-06 — Implement Source Integration, RecordSet, and registered operations

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-05 |
| Unlocks | DAC-07 |
| Primer IDs | D-01–D-09, F-01–F-12, C-05, H-01–H-03, P-02 |
| Production writes | `src/Koan.Core/KoanApplicationBuilder.cs` and its existing `ServiceCollectionExtensions.cs` overload owner; `src/Koan.Data.Abstractions/**`, `src/Koan.Data.Core/**`, `tests/Suites/Data/Core/**`, affected shared TestKit tests, and initiative evidence; Family/Adapter bindings remain test seams |
| Owner | Framework contracts/orchestration; Family/Adapter binding seams only |

## Meaningful outcome

Applications can safely explore an external source and execute bounded business-keyed reads without adopting Entity
persistence or leaking provider response types.

Application intent, complete public expression, guarantee/correction, placement, coalescence, and ergonomics are
frozen in [DAC-06-explore.md](../evidence/framework/DAC-06-explore.md). The narrow Koan.Core allowlist amendment is
required by the ratified neutral `AddKoan(koan => ...)` owner; Data cannot define a parallel application root.

## Required work

1. Implement the DAC-02 source handle and Source Integration conformance kind independently of Entity repositories.
2. Implement opaque source-bound container references; bounded list/continuation, safe resolve/ambiguity, describe,
   sample, and explicit provider-native inspection extension seams.
3. Implement the closed neutral value algebra, ordered/duplicate-aware `RecordSet`, missing/null distinction, DTO
   projection, deterministic accounting, positive limits, completion reasons, and additional-result-channel failure.
4. Replace dictionary→JSON→DTO materialization with compiled ordinal conversion. Reuse one materializer across
   inspection, registered reads, and any applicable Direct result path.
5. Implement the compact `Query`/`Scalar` registration surface over one immutable operation catalog, duplicate
   rejection, typed parameter plans, effect/result/delivery axes, source-owned execution lane, bounds, exact scalar
   cardinality, and telemetry. Provider binding extensions add only native payload decisions and do not repeat source,
   read, query, or provider context.
6. Adapters contribute native bindings only. Opaque bindings require provider-enforced read lanes; text prefixes never
   establish effect.
7. Keep Direct/provider-native operations as explicit expert surfaces under the same source ceiling.

## Evidence anchors

- primer §§1–3
- `src/Koan.Data.Core/Direct/DirectSession.cs`
- `src/Koan.Data.Abstractions/IRawQueryRepository.cs`
- source routing/policy plan produced by DAC-04
- current provider discovery folders, which are endpoint discovery rather than container inspection

## Verification

- Consumer compile tests reproduce every primer example.
- Fake provider modules prove flat, hierarchical, virtual, ambiguous, missing, truncated, and non-record containers.
- Record oracle covers duplicate names, missing/null, nested values, byte/value/duration limits, cancellation, provider
  partials, additional channels, DTO conversions, and no second materialization walk.
- Named-operation negatives cover duplicate catalog, parameters, Unknown effect, lane bypass, scalar cardinality,
  external artifact provisioning, segmentation, replay, and redaction.

## Definition of done

- [x] Every D/F Framework cell is executable and green; provider cases are ready for gold adapters.
- [x] The shared result hot path has no dictionary/JSON round trip or provider runtime values.
- [x] Source-only integration works without an Entity repository shim.

Verification: [DAC-06-verification.md](../evidence/framework/DAC-06-verification.md).

## Stop conditions

Stop if the neutral algebra cannot represent a provider result without flattening; preserve it behind a native surface
rather than widening `object?` arbitrarily.
