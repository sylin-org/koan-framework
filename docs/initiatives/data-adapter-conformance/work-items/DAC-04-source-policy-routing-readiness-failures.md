---
type: SPEC
domain: data
title: "DAC-04 Align Source Policy, Routing, Readiness, and Failure Ownership"
audience: [architects, maintainers, developers, ai-agents]
status: passed
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: Framework source-plan and lifecycle remediation prompt
---

# DAC-04 — Align source policy, routing, readiness, and failure ownership

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-50 |
| Unlocks | DAC-05 |
| Primer IDs | A-02–A-09, C-01–C-06, G-01–G-05, H-01–H-03, P-01, P-03 |
| Production writes | only `src/Koan.Data.Abstractions/**`, `src/Koan.Data.Core/**`, `tests/Suites/Data/Core/**`, affected shared TestKit tests, and initiative evidence; no Family or Adapter production paths |
| Owner | Framework |

## Meaningful outcome

An application declares source lifecycle/access once; every Entity, Direct, inspection, named-operation, transaction,
and provider-extension path consumes the same immutable ceiling and rejects before forbidden work.

## Required work

1. Compile typed source configuration, route/election, `StorageLifecycle`, `Access`, read lanes, capabilities, and
   credentials/physical-client identity into a host-scoped immutable plan.
2. Put the effect/policy gate before Entity callbacks, readiness, transaction/resource creation, cache, and provider I/O.
3. Separate reachability, declared-shape validation, and explicitly authorized provisioning with distinct single-flight
   state. An external source cannot implicitly create on connection/file open.
4. Remove business-operation probe/provision/replay and message-text shape classification from the shared path.
5. Introduce the ratified stable failure/outcome/retry-disposition contract and exact adapter translation seam.
6. Make nested context, Direct/connection override, transactions, batches, instructions, and provider extensions
   monotonic; no runtime argument can elevate the source plan.
7. Preserve exact provider election and current useful normalized routing behavior.

## Evidence anchors

- `src/Koan.Data.Core/DataSourceRegistry.cs`
- `src/Koan.Data.Core/RepositoryFacade.cs`
- `src/Koan.Data.Core/Adapters/DataAdapterReadinessExtensions.cs`
- `src/Koan.Data.Core/AdapterResolver.cs` and `Routing/**`
- `src/Koan.Data.Core/Direct/**` and `Transactions/**`

## Verification

- Red-first policy tests for all four lifecycle/access cells and every alternate path.
- Provider spy proves read-only/external rejection occurs before callbacks, readiness, resource creation, and dispatch.
- Concurrent readiness/provisioning, cancellation-detach, failure-not-cached, host-isolation, and disposal facts.
- Mutation test reorders the gate after readiness and must fail.

## Definition of done

- [x] Every named Framework row is PASS with required evidence or has a precise DAC-04 child blocker.
- [x] Operation replay and message-text lifecycle classification are unreachable.
- [x] One immutable source plan feeds diagnostics and downstream execution.
- [x] No production adapter contains a temporary policy workaround.

## Result

Framework ownership and chokepoints are complete. Provider-native shape, resource, cancellation, transaction,
diagnostic, and warm-path cells remain explicit child proofs for the gold rewrites and their owning later cards; see
`evidence/framework/DAC-04-verification.md`. No adapter production path was changed for this card.

## Stop conditions

Stop if a provider must change public behavior to compile; record an Adapter row for its later audit rather than
smuggling provider logic into Framework.
