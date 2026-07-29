---
type: SPEC
domain: data
title: "DAC-09R-02 Source freeze and bounded ownership"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: A-02 G-08 P-01 P-03 Framework remediation
---

# DAC-09R-02 — Freeze source decisions and bound host-owned state

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-01 |
| Unlocks | DAC-09R-03 |
| Required primer profiles/IDs | A-02, G-08, P-01, P-03 |
| Production writes | Allowed only for the source/runtime owner below |
| Allowed paths | `src/Koan.Data.Core/DataSourceRegistry.cs`; `src/Koan.Data.Core/DataService.cs`; `src/Koan.Data.Core/DataRuntimeOptions.cs`; `src/Koan.Data.Core/Infrastructure/Constants.cs`; `src/Koan.Data.Core/Runtime/BoundedSingleFlightCache.cs`; `src/Koan.Data.Core/ServiceCollectionExtensions.cs`; `src/Koan.Data.Core/SourceIntegration/Runtime/DataSourceIntegrationService.cs`; focused Data Core source/hosting tests; card evidence/ledgers |
| Forbidden paths | Adapters, mapping, Direct execution, claims, diagnostics, unrelated work |
| One semantic owner | Framework source decision and host lifetime |

## Meaningful outcome

An application declares a finite source set once and every operation in that host observes the same route and policy,
with deterministic resource ownership under concurrent first use.

## User contract

- **Application expression:** `services.AddKoan(koan => koan.Data.Source("LegacyErp")...)`.
- **Complete intent surface:** declare every source during composition; configure explicit finite cache bounds when the
  defaults are unsuitable.
- **Guarantee:** source declarations freeze with composition; admitted route/repository/integration state is bounded,
  single-flight, host-owned, and disposed exactly once.
- **Correction:** a late/replacement source or exhausted bound rejects before creating provider state and names the
  composition or bound correction.
- **Public concepts:** existing source declaration plus typed runtime bounds; no runtime registration vocabulary.

## Execution

Reproduce split-brain replacement and concurrent duplicate creation, freeze registration after composition, bound each
key space, publish lazies single-flight, and deterministically dispose failed/losing/retired resources.

## Verification

Two-host, concurrent-first-use, capacity, failed-lazy, replacement-rejection, and disposal tests pass with no provider
activation before admission.
