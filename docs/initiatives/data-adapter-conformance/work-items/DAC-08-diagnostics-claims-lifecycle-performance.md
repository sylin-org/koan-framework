---
type: SPEC
domain: data
title: "DAC-08 Align Diagnostics, Claims, Lifecycle, and Performance"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: framework-owned diagnostics, claims, resource ownership, scenarios, and benchmark grammar
---

# DAC-08 — Align diagnostics, claims, lifecycle, and performance

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-07 |
| Unlocks | DAC-09 |
| Primer IDs | G-01–G-09, H-01–H-06, P-01–P-06 plus claim publication rules |
| Production writes | only `src/Koan.Data.Abstractions/**`, `src/Koan.Data.Core/**`, `src/Koan.Testing/**`, shared TestKit/benchmark/facts tests, and initiative evidence; no connector behavior |
| Owner | Framework, with native Adapter receipts |

## Meaningful outcome

The application and operator can inspect the same immutable decisions that execution uses, while lifecycle, failures,
resources, and hot paths remain objectively testable.

## Application contract

```csharp
var source = Data.Source("LegacyErp");
var description = source.Describe();
var explanation = source.Explain("orders.recent");
var diagnosis = await source.Doctor(ct);
```

`Describe` and `Explain` are pure redacted projections of the exact frozen decisions used by execution. `Doctor` is
active but non-mutating. The executable adapter claim declaration is the sole input to runtime capability
publication and TestKit applicability. See `evidence/framework/DAC-08-explore.md` for placement, coalescence,
ergonomics, privacy, and host-ownership decisions.

## Required work

1. Make the executable claim declaration feed runtime capabilities, facts, health, source descriptions, TestKit
   applicability, packet summaries, and product-surface inputs.
2. Implement pure `Describe`/`Explain` and active non-mutating `Doctor` over frozen plans and exact corrections.
3. Complete stable public failure taxonomy, restricted native evidence, commit outcome, retry disposition, and redaction.
4. Make host/client/pool/cache ownership explicit, bounded, isolated, and disposable. Eliminate mutable process-static
   plan/readiness state and unbounded structural caches.
5. Add standard fault, cancellation, pool-saturation, two-host, restart, durability, isolation, and soak modules.
6. Add cold/warm benchmark cells from primer §7, capturing allocations, provider dispatch count, elapsed time, and
   provider work. Thresholds are fixture/version-specific.
7. Generate the one-page responsibility map and fail P-06 on duplicated Framework mechanics.

## Evidence anchors

- `src/Koan.Data.Core/DataDiagnostics.cs` and health/reporting types
- `src/Koan.Data.Abstractions/Capabilities/DataCaps.cs`
- `src/Koan.Data.Abstractions/RepositoryQueryResult.cs`
- adapter health contributors and host lifecycle tests
- DAC-03 claim/packet control plane

## Verification

- Facts/health/Describe/Explain tests compare the exact decision identities and claim set.
- Redaction tests inject secrets, parameters, business values, tenant/source identifiers, and raw native errors.
- Fault/lifecycle tests cover two hosts, disposal, saturation, cancellation, timeout, commit uncertainty, restart,
  durability, isolation, and bounded soak.
- Benchmark mutation adds reflection/readiness I/O/unbounded caching on a warm path and must fail.

## Definition of done

- [x] All applicable G/H/P Framework cells are green.
- [x] Every capability claim can be traced from runtime declaration to verifier and public projection.
- [x] Provider certification can capture exact native receipts without leaking restricted evidence.

## Stop conditions

Stop if public facts would expose secrets/provider runtime objects or if a global performance threshold is proposed
without a pinned provider-relative basis.
