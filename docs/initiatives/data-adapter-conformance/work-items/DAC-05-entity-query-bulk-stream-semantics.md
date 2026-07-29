---
type: SPEC
domain: data
title: "DAC-05 Align Entity, Query, Bulk, Batch, and Stream Semantics"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: Framework Entity execution and receipt semantics; provider-native proofs delegated explicitly
---

# DAC-05 — Align Entity, query, bulk, batch, and stream semantics

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-04 |
| Unlocks | DAC-06 |
| Primer IDs | B-01–B-09, C-03, G-05–G-06, P-02, P-04 |
| Production writes | only `src/Koan.Data.Abstractions/**`, `src/Koan.Data.Core/**`, `tests/Suites/Data/Core/**`, affected shared TestKit tests, and initiative evidence; no provider adapters |
| Owner | Framework, with explicit Family execution seams |

## Meaningful outcome

Ordinary Entity operations have one set of identity, lifecycle, query, paging, bulk, conflict, and cancellation
semantics regardless of provider realization.

## Required work

1. Audit and align `IDataRepository`, `IQueryRepository`, `RepositoryFacade`, Entity verbs, batches, bulk interfaces,
   conditional writes, count, transactions, and provider-bounded streams.
2. Preserve Data ownership of filter pushdown splitting, residual evaluation, final sort/page/projection/count shaping,
   and accurate execution receipts. Adapters receive only the complete pushable definition.
3. Enforce get-many cardinality/order/null semantics, identity codecs, lifecycle callbacks, scoped remove strategies,
   honest count/atomicity, deferred batch mutation, and missing/conflict outcomes.
4. Distinguish framework coordination from native atomic transactions. Current best-effort sequential coordination
   cannot publish a native atomicity claim.
5. Reject unsupported operations before unbounded work or partial mutation. A client scan/page or in-memory handled
   report cannot satisfy provider-bounded/optimized claims.
6. Ensure cancellation and cleanup flow through every bulk, batch, query, and stream path.

## Evidence anchors

- `src/Koan.Data.Abstractions/IDataRepository.cs`, `IQueryRepository.cs`, and capability interfaces
- `src/Koan.Data.Core/RepositoryFacade.cs`
- `src/Koan.Data.Core/Querying/FilterPushdownCoordinator.cs`
- `src/Koan.Data.Core/Querying/QueryStreamCoordinator.cs`
- `src/Koan.Data.Core/BatchExtensions.cs` and `Transactions/**`
- existing Convergence and AdapterSurface tests

## Verification

- Shared CLR oracle over all declared operators and boundary values.
- Red-first cases for missing/invisible get-many slots, residual+page ordering, false handled receipts, partial bulk,
  deferred mutation races, cancellation, and outcome unknown.
- Mutation checks remove residual evaluation and falsify provider paging receipts; shared tests must fail.
- Focused Data suites and full solution build.

## Definition of done

- [x] Framework-owned B/G/P rows are green and provider-dependent cases have exact adapter seams.
- [x] Every stronger capability is coupled to its receipt and verifier.
- [x] No shared path silently scans/materializes to imitate native support.

Verification: [DAC-05-verification.md](../evidence/framework/DAC-05-verification.md).

## Stop conditions

Stop if completing a row requires a provider-specific algorithm in Core or changes the primer's explicit ORM boundary.
