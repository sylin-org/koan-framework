---
id: DATA-0134
slug: relational-filters-keep-the-oracle-null-semantics
domain: Data
status: Accepted
date: 2026-10-21
title: Relational filters keep the oracle's two-valued null semantics for Ne, In, Nin and Not
related:
  - ARCH-0079
  - DATA-0114
---

# DATA-0134: Relational filters keep the oracle's two-valued null semantics for Ne, In, Nin and Not

## Context

One Entity query means the same thing on every store, and the in-memory oracle (`InMemoryFilterEvaluator`) defines
that meaning. The oracle is two-valued: a predicate over a null or missing field is false, `Ne` is `!Eq`, `Nin` is
`!In`, `In` asks `Eq` of each member (so a null member matches a null value), and `Not` is `!inner`.

`SqlFilterTranslator`, which every relational adapter shares (SQLite, PostgreSQL, SQL Server, MySQL, DuckDB,
Firebird), lowered the same nodes under SQL's three-valued logic:

- `Ne v` became `col <> @p`, which is unknown, and so drops the row, when `col` is NULL. A comment claimed the oracle
  also answers false there; it answers true.
- `Not x` became `NOT (x)`, and `NOT` of unknown stays unknown, so `!(x.Score > 80)` and `$nor` dropped null rows.
- `In [null, v]` became `col IN (@p0, @p1)`, which never matches NULL; `Nin [null, v]` became
  `col IS NULL OR col NOT IN (...)`, which matched only the null row, the inverse of the oracle.

Measured on SQLite before this change with the shared corpus (w2 has a null `Score`): `$ne 100` gave
`[w3,w4,w5]` against the oracle's `[w2,w3,w4,w5]`; `$in [null,100]` gave `[w1]` against `[w1,w2]`; `$nin [null,100]`
gave `[w2]` against `[w3,w4,w5]`; `$not {$gt 80}` and `$nor [{$gt 80}]` gave `[w4]` against `[w2,w4]`. Ledger NK-020,
NK-021, NK-051. The same predicate evaluated as a residual answered with the oracle, so one store could return two
answers to one query depending on what else the filter held.

## Decision

The translator emits SQL whose answer is the oracle's, for every node that negates or tests set membership:

1. `Ne v` (non-null `v`) is `(col IS NULL OR col <> @p)`. `Ne null` stays `col IS NOT NULL`.
2. `Not x` is `(CASE WHEN x THEN 1 ELSE 0 END = 0)`: unknown folds to false before the negation. The form is accepted
   by every relational dialect Koan ships, so no dialect hook is needed.
3. `In` and `Nin` split a null member out of the set: `In [null, v]` is `(col IS NULL OR col IN (@p))`,
   `Nin [null, v]` is `(col IS NOT NULL AND col NOT IN (@p))`, a set of only null is `IS NULL` / `IS NOT NULL`, and a
   set with no null keeps the existing forms.

`SupportsNegation` stays true and `Ne` stays pushable; the store keeps doing the work.

## Compatibility

Behavioural, on every relational adapter. Queries using `!=`, `!`, `$ne`, `$not`, `$nor`, or `In`/`Nin` with a null
member now return the rows the oracle, InMemory, Json and MongoDB return: rows with a null or missing value appear in
`!=` and negated results, and set membership treats null as a value. `x != v` over an indexed column becomes an `OR`
with `IS NULL`, which some planners serve less directly; negations were never index-friendly. No public signature or
stored format changes.

## Evidence

- `FilterConvergence.Cases()` (adapter-surface kit) gains `scalar-ne-on-nullable`, `scalar-in-with-null`,
  `scalar-nin-with-null`, `not-gt-on-nullable` and `nor-on-nullable`. `SqliteFilterConvergenceSpec` failed on all five
  before this change and passes after; `DuckDbFilterConvergenceSpec` passes after. PostgreSQL, SQL Server, MySQL,
  Firebird, Couchbase and CouchDB inherit the cases; they were compiled, not run (no container runtime). MongoDB runs
  them against a local server.
- `SqlFilterNullSemanticsSpec` (Data.Relational) pins the emitted shapes; six of its eight cases failed before.
