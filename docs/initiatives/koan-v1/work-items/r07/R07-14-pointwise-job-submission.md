---
type: SPEC
domain: framework
title: "R07-14 - Pointwise Jobs Submission"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.17
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: constrained Jobs submission over scalar, finite, and provider-bounded Entity sources
---

# R07-14 — Pointwise Jobs submission

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-13
- Unlocks: AI Embed/Index pointwise inventory
- Owner: Jobs ledger acceptance, work-item persistence, partial outcomes, and wake cadence

## Meaningful outcome

One business intent reads naturally for a job Entity, a finite selection, or a provider-bounded
stream, while each item remains an independent durable job:

```csharp
JobHandle one = await order.Job.Submit(Order.Notify, ct);

JobSubmission selected = await orders
    .Where(order => order.Ready)
    .Submit(Order.Notify, ct);

JobSubmission streamed = await Order
    .QueryStream(order => order.Ready)
    .Submit(Order.Notify, ct);
```

The scalar returns its inspectable handle. A source returns one fixed-size ledger-acceptance summary;
it never retains a handle per item or implies that handlers completed.

## Architecture

- `.Job` remains the scalar work-item surface; `.Jobs` remains the type-level trigger/query/cancel/
  status control plane. Direct source `Submit` is the only pointwise collection terminal.
- Scalar and source paths share one `JobCoordinator` acceptance operation: resolve policy and
  coalescing, persist the work Entity, append the ledger record, then announce accepted work.
- `EntityCardinality.Many/Stream` owns one-pass normalization and cancellation. The coordinator owns
  Jobs meaning and accepts sequentially, preserving source order and multiplicity with bounded
  producer memory.
- One terminal snapshot captures logical context before the first await and restores it around
  deferred enumeration and every work-item save. Tenant, subject, and future axes remain carrier-owned.
- New work wakes the worker every bounded internal interval (and per item in inline mode); the ledger
  and poll remain the correctness path. Ambient transactions suppress premature wake/drain.
- `JobSubmission` contains counters only: `Submitted`, `Coalesced`, `Failed`, `Accepted`, source
  completion, work/action identity, and `PendingCommit`.
- `JobSubmissionException` distinguishes source from acceptance failure;
  `JobSubmissionCanceledException` remains catchable as `OperationCanceledException`. Both retain the
  confirmed prefix.

Sequential ledger append is deliberate. It trades the old final bulk append for exact confirmed-prefix
accounting, bounded memory, and the same per-item persistence semantics scalar submission already had.

## Principal deletion

- duplicate `JobStatics<T>.Submit(IEnumerable<T>)` grammar;
- duplicated scalar/source preparation and acceptance logic;
- coordinator materialization of every pending `JobRecord`;
- opaque `int` source result;
- ad hoc Jobs ambient-host error construction; and
- Jobs-owned singleton-id literal outside its constants boundary.

## Delight contract

- Developers read business selection followed by `Submit`; the result says what the ledger accepted,
  including coalescing and transaction contingency.
- Coding agents discover scalar/set/stream syntax through the normal Entity and Jobs namespaces, with
  invalid receivers rejected at compile time and no repository/queue/provider vocabulary to invent.
- Operators and reviewers keep the ledger as truth, the same health/status/history surfaces, and
  bounded wake facts; source convenience does not create a second queue or hidden execution contract.
- Context cannot drift mid-source, so a terminal invoked in tenant A never silently submits later
  items under another ambient flow.

## Acceptance

- Scalar behavior remains green through the shared acceptance operation.
- Finite and async sources preserve order, multiplicity, one-pass enumeration, and bounded
  backpressure.
- Declared idempotency reports coalesces separately from new ledger rows.
- Source failure and cancellation expose the exact confirmed prefix through typed exceptions.
- An ambient transaction reports `PendingCommit`; commit makes work visible and rollback does not.
- One captured tenant context applies to deferred enumeration and every accepted item.
- Module presence, absence, removal, invalid receiver, and all-module collision cells compile as
  intended.
- A real application consumes `JobSubmission.Accepted` rather than assuming input count equals queue
  acceptance.

## Explicit non-claims

- collection atomicity, handler completion, execution ordering, or exactly-once effects;
- constant ledger size or suitability of one job per row for very large/unbounded sources;
- per-item handles retained in the source receipt;
- every durable provider or distributed topology;
- exact provider-side outcome of the current append call when that provider throws; or
- Web/MCP business authorization from the Entity terminal itself.

## Evidence

- `Koan.Jobs.Tests` passes 82/82, including five new async/coalescing/source-failure/item-failure/
  cancellation source cells and the shared finite-source behavior inherited by provider suites.
- Entity Language passes 25/25 for Jobs scalar/set/stream presence, absence, module removal, invalid
  receiver rejection, and all-module composition.
- Focused SQLite submission/transaction evidence passes 2/2; the last complete SQLite baseline remains
  79/79 and is not re-claimed as rerun here.
- Focused durable tenancy context sealing passes 1/1. Its project retains two unrelated pre-existing
  XML-documentation warnings outside this slice.
- `Koan.Jobs` builds warning-as-error with zero warnings/errors; the changed source dogfood builds and
  returns confirmed ledger acceptance. SnapVault's focused upload-progress contract passes 3/3 after
  a normal restore refreshed its versioned test output graph.
- The independently versioned Jobs 0.19 owner packs with its README/XML documentation and exact
  internal dependency floors; package inventory remains 112 owners.
- Documentation lint reports 0 errors / 1580 pre-existing-or-front-matter warnings. Skills lint passes
  20/20 with zero warnings, its marked Jobs example compiles 1/1, and stale-surface, diff, and privacy
  checks close without a release-certification run.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-16
- Follow-up: inventory AI pointwise `Embed`/`Index`; open no child until one business-proven operation
  and its backend-negotiation boundary are elected.
- Reviewer: Codex implementation under maintainer standing approval.
