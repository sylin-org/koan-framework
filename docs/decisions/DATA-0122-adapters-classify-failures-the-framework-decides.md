---
id: DATA-0122
slug: adapters-classify-failures-the-framework-decides
domain: Data
status: Accepted
date: 2026-08-22
title: Adapters classify a failure, the framework decides what it earns
related:
  - DATA-0119
  - DATA-0121
---

# DATA-0122: Adapters classify a failure, the framework decides what it earns

## Context

Eight concurrent job claimers deadlock SQL Server roughly one full suite run in five. The victim's
`SqlException` — *"chosen as the deadlock victim"* — propagated out of `JobOrchestrator.DrainAsync`, which
wraps its loop in `try`/`finally` with no `catch`, and ended the drain. Deadlock is the one SQL Server failure
whose documented remedy is literally *"rerun the transaction"*.

Koan had nowhere to put that fact. `IDataFailureClassifier`, `DataFailure`, `DataFailureKind`, the commit and
replay dispositions and the correction text all existed in `Koan.Data.Abstractions.Failures` — **with zero
producers and zero consumers across `src/`**. A designed seam, never wired; the third found in one cycle after
the schema orchestrator (PMC-040) and the AOT-clean ADO surface (PMC-047).

The apparent choice was "wire the seam or bolt a retry onto the claim". It is a false one: `Koan.Jobs` catching
`SqlException` 1205 puts a store's error numbers inside framework policy, which is precisely the boundary
DATA-0119 draws. A retry done properly *is* the seam.

## Decision

**The adapter says what a native failure was. The framework decides what that meaning earns. Neither half can
be written by the other.**

- **The adapter classifies, by code and never by message.** `IDataFailureClassifier.TryClassify` already
  carried that rule in its doc comment; a message is localized and its text is not a contract.
  `SqlServerFailureClassifier` recognises error 1205 across the whole `SqlException.Errors` collection rather
  than just `Number`, which reports only the first.
- **Every field of a `DataFailure` is a claim a caller may act on**, so each is answered conservatively.
  A deadlock victim is `Conflict` / `NotCommitted` / `RequiresIdempotency`. `NotCommitted` is the load-bearing
  half — SQL Server has already rolled the victim back — while the retry disposition is deliberately weaker
  than the rollback alone would permit, because the enum has no "always safe" and overstating safety is the
  expensive direction to be wrong in.
- **The framework's half retries nothing.** `DataFailurePolicy.MayRetryIdempotent` answers whether an operation
  *the caller knows to be idempotent* may be attempted again, gated first on the store saying nothing
  committed. `RequiresIdempotency` is a statement about the store, not about the caller; only the caller knows
  whether its operation can run twice.
- **`BeforeDispatchOnly` is honoured strictly**, not folded in with the looser case: it permits a retry only
  where the operation never reached the store.
- **An unclassified failure stays raw.** `TryClassify` returning `false` leaves a failure exactly as it was
  before any of this existed.

The claim path is the first consumer, and may be, because it ends in a conditional write that either wins or
reports that another owner did — a single `UPDATE ... WHERE identity AND (Status = @p) AND (Owner IS NULL)`
succeeding on `rowcount == 1`. That property is established separately (PMC-048) and is what makes the retry
safe rather than merely convenient.

## Consequences

- A deadlocked claim is retried instead of ending a drain. The bound is three attempts: two suffice for a
  deadlock, because the winner has committed by the time the victim rolls back, and the third is headroom for a
  second collision rather than a policy of grinding.
- **Exactly one failure is classified, deliberately.** PostgreSQL's `40P01`, MySQL's `1213` and SQLite's
  `SQLITE_BUSY` are each a real classification and each should arrive with its own reproduction rather than by
  analogy. A classifier written from documentation rather than from a captured failure is a guess with a
  contract's authority.
- Extending the seam is now additive: a new classifier is one type and one `TryAddEnumerable` registration.
- The safety property to preserve is the commit-outcome gate. A retry disposition is permission, not proof; if
  a store cannot say the operation failed to commit, repeating it is how one write becomes two. That case is
  pinned by a spec asserting `Committed` and `Unknown` outcomes are never retried whatever their disposition
  claims.

## References

- `src/Koan.Data.Abstractions/Failures/` — the pre-existing seam
- `src/Koan.Data.Core/Failures/DataFailurePolicy.cs` — the consumer half
- `src/Connectors/Data/SqlServer/Runtime/SqlServerFailureClassifier.cs`
- `src/Koan.Jobs/DataJobLedger.cs` — `ClaimNext` over `ClaimOnce`
- `docs/initiatives/koan-v1/POST-CYCLE-TODO.md` — PMC-048, PMC-056
