# JOBS-0006: Trailing-Edge Coalesce Semantics

**Status**: Accepted
**Date**: 2026-06-11
**Deciders**: Enterprise Architect
**Scope**: Koan.Jobs -- IJobLedger.FindActiveByCoalesceKey, InMemoryJobLedger, DataJobLedger
**Related**: JOBS-0002 (coalescing baseline), JOBS-0005 (orchestrator rebuild)

---

## Context

`FindActiveByCoalesceKey` previously matched any non-terminal row (Queued or Running). This meant:

- A trigger arriving while a job is Running is absorbed by the Running row and returns its handle.
- When that Running job completes, no follow-up execution fires.
- The last change before quiescence is permanently lost.

This behavior is correct for strict deduplication (at-most-once semantics) but wrong for
debounced refresh composites, where consumers rely on "always process the last trigger after the
current run settles."

Three options were evaluated:

- **(a) Re-arm-on-settle**: a submit against a Running match arms one follow-up record in the ledger.
  Complex orchestrator surgery; creates a hidden second record the consumer cannot observe until settle.
- **(b) Queued-only matching**: `FindActiveByCoalesceKey` matches only `Queued` rows.
  A submit while Running queues a new record immediately. At most 1 Running + 1 Queued per coalesce key.
- **(c) Level-triggered sweep helper**: document a first-class sweep pattern and drop run-time coalescing.
  Correct for sweep workloads but does not address per-entity debounce.

## Decision

**Option (b): queued-only matching.**

`FindActiveByCoalesceKey` matches `Status == Queued` only. A submit arriving while the
same-keyed job is Running is NOT collapsed; it queues a new record. The result is a
trailing-edge debounce pattern: at most one queued follow-up per coalesce key, which runs
after the current execution completes.

## Consequences

**Safe for existing consumers**: consumers that relied on strict deduplication (no follow-up
after a mid-run trigger) now get one extra execution per overlapping trigger window. This is
the correct behavior for any consumer that uses coalescing for write-convergence (e.g. Work.Refresh
composites); it is a no-op difference for consumers that emit triggers only before the first run.

**Bounded fan-out**: the at-most-1-queued cap prevents unbounded pile-up during sustained trigger
storms; each coalesce key holds at most one in-flight execution and one queued follow-up at any
given moment.

**Consumer guidance**: use `[JobIdempotent]` with a coalesce key for debounced refresh composites.
The framework guarantees trailing-edge delivery without requiring application-side bookkeeping.

## Implementation

Changed in `InMemoryJobLedger.FindActiveByCoalesceKey` and `DataJobLedger.FindActiveByCoalesceKey`
on 2026-06-11 (K2 fixes batch). Both ledgers now filter on `r.Status == JobStatus.Queued` instead
of `!r.IsTerminal`. Regression covered by `trailing_submit_during_run_queues_a_follow_up_execution`
in `JobBehaviorSuite`.
