---
id: DATA-0136
slug: a-scalar-column-is-compared-in-the-form-it-is-written
domain: Data
status: Accepted
date: 2026-10-21
title: A real scalar column is compared and keyed in the form the store writes it
related:
  - DATA-0100
  - DATA-0134
---

# DATA-0136: A real scalar column is compared and keyed in the form the store writes it

## Context

DATA-0100 makes composite scalars comparable by writing them in an order-preserving form: DateTimeOffset as
fixed-width UTC text, TimeSpan as ticks, DateOnly and TimeOnly as fixed text. SQLite, DuckDB and Firebird apply that
form to real (non-nested) scalar columns through `RelationalEntityPlan.EncodeScalar` on every write. Two read paths did
not:

- `SqlFilterTranslator` deliberately skipped the comparand encoding for a non-nested scalar binding and bound the
  value raw. Microsoft.Data.Sqlite then binds a DateTimeOffset as `yyyy-MM-dd HH:mm:ss.FFFFFFFzzz` and a TimeSpan as
  text, which never equal and do not order against what was written.
- The identity predicate behind Get, GetMany, Delete, DeleteMany, conditional replace and conditional delete bound the
  key raw, in SQLite, DuckDB and Firebird alike.

So an explicit map with a DateTimeOffset or TimeSpan column, or any entity keyed by one, stored rows it could not find.
Measured on SQLite before this change: `Query(x => x.At == at)` returned nothing for a row saved with that value, and
`Get` by the same instant at another offset returned null. Ledger NK-058. `SqliteEntityPlan`'s comment claimed filters
already encoded the same way.

## Decision

1. `RelationalEntityPlan.Filter()` builds the table's translator, and every relational adapter now takes its
   translator from it. The plan hands the translator its own `EncodeScalar` through a new
   `SqlFilterTranslator(dialect, mapping, managedResolver, encodeColumn)` overload, applied to the comparand of a real
   scalar column after the binding's codec. Nested document paths keep `ComparableScalarEncoding.EncodeComparand`.
2. Identity predicates bind `plan.Parameter(value)`, the write path's own encoding, in SQLite, DuckDB and Firebird.
3. Adapters whose `EncodeScalar` is the identity (PostgreSQL, SQL Server, MySQL) emit and bind exactly what they did.

## Compatibility

Behavioural on SQLite, DuckDB and Firebird, for explicit maps with DateTimeOffset, TimeSpan, DateOnly, TimeOnly or enum
scalar columns and for entities keyed by such a type: filters and key lookups now find the rows Koan wrote. Rows written
outside Koan in another format are not found by an equality they matched before only by accident. Stored data is
unchanged, because writes already encoded. The existing three-argument constructor is unchanged.

## Evidence

`SqliteScalarColumnEncodingSpec` (Connector.Sqlite): an explicit map with DateTimeOffset and TimeSpan columns converges
for `==`, the same instant at another offset, `>` and `<`; a DateTimeOffset-keyed entity is found by `Get` at either
offset, queried, and deleted. Both specs failed before this change. DuckDB, Data.Relational and the SQLite suites pass;
the PostgreSQL, MySQL, SQL Server, Firebird and CockroachDB suites compile.
