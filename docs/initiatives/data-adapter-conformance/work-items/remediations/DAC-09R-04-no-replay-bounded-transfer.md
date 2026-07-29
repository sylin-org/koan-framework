---
type: SPEC
domain: data
title: "DAC-09R-04 No replay and bounded transfer"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: B C G P transfer and fallback remediation
---

# DAC-09R-04 — Select bounded fallback before dispatch and never replay

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-09R-03 |
| Unlocks | DAC-09R-05 |
| Required primer profiles/IDs | B-03, B-04, B-08, C-04, G-04, P-02, P-04 |
| Production writes | Allowed only for Framework fallback/transfer execution |
| Allowed paths | `src/Koan.Data.Core/RepositoryFacade.cs`; `src/Koan.Data.Core/Data.cs` and `Model/Entity.cs` (transfer entry points only); `src/Koan.Data.Core/Transfers/**`; removal of the parallel `SetMoveBuilder`; `src/Koan.Data.Core/Querying/**`; compile-only downstream call-site migrations; transfer/query receipt abstractions when strictly required; focused transfer/fallback tests; card evidence/ledgers |
| Forbidden paths | Connectors, source registry, Direct, public diagnostics, unrelated work |
| One semantic owner | Framework fallback and transfer execution plan |

## Meaningful outcome

Bulk movement and semantic fallbacks have an explicit finite source-work bound and cannot repeat an operation after a
provider may have observed it.

## User contract

- **Application expression:** `await Todo.Copy().To(source: "Archive").Batch(500).Run(ct)` with an optional predicate.
- **Complete intent surface:** source, destination, mode, predicate, batch/page bound, and conflict policy.
- **Guarantee:** capability selection occurs before dispatch; reads are provider-paged/cancellable; one candidate is
  shaped once; every dispatch has validated filter/count/outcome facts; ambiguous exceptions never trigger replay.
- **Correction:** unsupported paging/predicate/clear fails before provider work and identifies the narrower operation or
  explicit bound required.
- **Public concepts:** reuse paging, receipt, and commit/outcome contracts; no hidden full-scan mode.

## Execution

Remove post-dispatch `NotSupportedException` fallbacks, compile predicates once, use provider-bounded pages, enforce
source and destination bounds, and prove single dispatch under success/fault/cancellation.

## Verification

The complete transfer suite, no-replay mutation tests, receipt validation, and bounded-candidate observations pass.
