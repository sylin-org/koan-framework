---
id: DATA-0129
slug: opaque-instructions-fail-closed-under-every-scope
domain: Data
status: Accepted
date: 2026-10-21
title: Opaque instructions fail closed under every scope, and a Read label is enforced by the store
related:
  - DATA-0105
  - DATA-0106
  - WEB-0074
  - DATA-0124
  - DATA-0128
---

# DATA-0129: Opaque instructions fail closed under every scope, and a Read label is enforced by the store

## Context

Three escape hatches carry text Koan cannot read: `QueryRaw`/`CountRaw`, instructions (`InstructionSql.*`,
`Data<T,K>.Execute(sql)`, `DataInstructions.Clear`) and Direct sessions. Only the first applied the whole scope
union. `RepositoryFacade.ExecuteAsync` refused an instruction only for compiled segmentation, and
`DirectSession.GuardDirect` likewise (ledger NK-046, NK-101, NK-066):

- under soft delete, a DataAxis predicate or a Web row filter, raw SQL instructions read and wrote across the scope
  while `QueryRaw` threw;
- `DataInstructions.Clear` went to the adapter's `DeleteAll`, which on SQLite, PostgreSQL, Mongo and Couchbase is an
  unscoped table wipe: a soft-delete entity was hard-deleted, hidden moderation rows went with the visible ones, and
  remove Lifecycle did not run;
- a Direct session ran while a Shared-mode isolation axis (an org or region field) was bound.

Separately (NK-037), the instruction effect is whatever the caller declares. Source policy trusts it, so a ReadOnly
source admitted `Execute("DELETE …", DataOperationEffect.Read)`, and SQLite ran every SQL instruction on its
ordinary read-write connection. The raw-read lane of WEB-0074 (SQLite `query_only` on an unpooled connection,
PostgreSQL `SET TRANSACTION READ ONLY`) existed and was not used.

Measured before this change on SQLite: under a pure predicate axis, `Execute<long>(Scalar("SELECT 1", Read))` and a
Write `Execute` ran; under a moderation axis `Clear` deleted the hidden row with the visible one; a soft-delete
`Clear` hard-deleted every row; a Direct scalar ran with the axis value bound; a Read-labelled `DELETE` deleted.

## Decision

1. **An opaque instruction fails closed under any active managed or read scope**, with the `QueryRaw` rule
   (`GuardRawAgainstActiveScope`). Only schema instructions that touch no rows are exempt: `EnsureCreated`,
   `SchemaEnsureCreated`, `SchemaValidate`. The existing segmentation refusal is unchanged.
2. **`Clear` is `DeleteAll`.** It refuses under an active scope like any opaque instruction, and otherwise runs as the
   facade's `DeleteAll`, so it carries the Write effect whatever the caller labels it, runs remove Lifecycle and
   deletes in bounded pages where `DeleteAll` would. On an entity whose deletes are overridden (soft delete) it is
   refused outright: inside `WithDeleted()` no read scope applies, and the lowered `DeleteAll` would re-soft-delete
   the same rows forever (ledger NK-100, not fixed here), while a wipe would bypass the override.
3. **A Read label is enforced by the store.** SQLite runs a Read SQL instruction on the raw-read connection
   (`query_only`, unpooled); PostgreSQL runs it inside `SET TRANSACTION READ ONLY`. A mislabelled write fails with a
   provider error and changes nothing.
4. **A Direct session refuses while a Shared isolation axis value is bound**: any row-scoped, ambient-stamped managed
   field whose value provider returns a value, alongside the segmentation check. A Direct session is untyped, so
   visibility predicates that differ per entity (soft delete, Web row filters) are not evaluated for it; they are
   enforced on the Entity surface, which now includes instructions.

## Compatibility

- Applications that ran raw SQL instructions on soft-delete, DataAxis or Web-filtered entities inside an active scope
  now receive `NotSupportedException`. Run them where the scope does not apply (for soft delete, inside
  `T.WithDeleted()`), or use the Entity surface.
- `Clear` on a soft-delete entity is refused instead of truncating; use `RemoveAll()` to soft-remove. `Clear` on other
  entities runs remove Lifecycle. A caller-declared `Read` effect on `Clear` no longer admits it on a ReadOnly source.
- A SQL instruction labelled `Read` that writes now fails on SQLite and PostgreSQL. On SQLite a Read instruction opens
  an unpooled connection, the cost `QueryRaw` already pays.
- Direct sessions throw while a Shared isolation axis value is bound.
- MySQL, SQL Server, Firebird and DuckDB still run Read-labelled SQL on an ordinary connection; they have no raw-read
  lane yet (follow-up).
- No public signature or stored format changes.

## Evidence

- `ReadFilterContributorSpec` (SQLite): opaque Read and Write instructions throw under a pure predicate axis while
  `EnsureCreated` still runs; `Clear` throws under a moderation axis and both rows survive; a Direct session throws
  while the axis value is bound and runs when it is not.
- `SqliteRawQueryBoundarySpec`: a Read-labelled `DELETE` fails with "readonly database" and the sentinel row survives;
  a Read scalar observes `query_only = 1`, a Write scalar `0`.
- `SoftDeleteSpec`: `Clear` throws while deleted rows are hidden and inside `WithDeleted()`, and both rows survive.
- All five failed before this change. The PostgreSQL fence compiles and follows the `QueryRaw` lane; it was not run,
  because no container runtime was reachable.
