---
id: DATA-0139
slug: guid-and-datetime-document-values-compare-as-document-text
domain: Data
status: Accepted
date: 2026-10-21
title: Guid and DateTime document values compare as the text the document holds
related:
  - DATA-0100
  - DATA-0136
---

# DATA-0139: Guid and DateTime document values compare as the text the document holds

## Context

DATA-0100 gives DateTimeOffset, TimeSpan, DateOnly and TimeOnly an order-preserving form. It does not govern Guid or
DateTime, so inside a JSON document they are the serializer's own text: a Guid as lower-case `D` text, a DateTime as
round-trip ISO text with a trimmed fraction and a zone suffix that depends on its Kind (`Z`, `+hh:mm`, or nothing). A
relational filter on such a property read that text and bound the CLR value, which each driver binds differently:

- PostgreSQL (Npgsql binds uuid and timestamp): `o.CustomerId == id` and `o.PlacedAt > cutoff` threw
  `operator does not exist: text = uuid` or `text > timestamp`. Ledger NK-052.
- SQLite (Microsoft.Data.Sqlite binds a Guid and a DateTime as text in another spelling): the same filters returned
  wrong rows silently. Measured before this change with the new corpus: Guid equality and `In` returned nothing, `!=`
  returned every row, DateTime equality returned nothing and ranges compared the wrong strings.
- DuckDB: DateTime ranges threw `Cannot compare values of type VARCHAR and type TIMESTAMP`.

MongoDB stores native BSON values and converged already.

## Decision

1. `IRelationalMappingDialect` gains two default members, `DocumentOperand(readSql, type)` and
   `DocumentComparand(value, type)`, which the translator applies to a scalar read out of a JSON document (a nested
   physical path). The defaults change nothing. Index expressions keep `Read`, so existing expression indexes still
   match equality reads.
2. `RelationalDocumentText` (Koan.Data.Relational) owns the comparable text. A Guid comparand is its `D` text. A
   DateTime is fixed-width wall-clock text to the tick, `yyyy-MM-ddTHH:mm:ss.fffffff`, and the DateTime operand is
   rewritten to the same form (first nineteen characters, fraction padded to seven digits, zone suffix dropped) with
   `substr`, `length`, `||` and `CASE`, portable across the three dialects. The oracle compares DateTime ticks and
   ignores Kind; so does the wall-clock text.
3. SQLite, PostgreSQL and DuckDB adopt both members. PostgreSQL compares the DateTime text under `COLLATE "C"`.

## Compatibility

Behavioural on SQLite, PostgreSQL and DuckDB for Guid and DateTime properties stored inside the document: filters that
threw now run, and filters that returned wrong rows on SQLite now return the oracle's rows. Explicit-map scalar columns
are unchanged. A DateTime comparison does not use an expression index (none matched it before). Collection operators
over `List<Guid>` and `List<DateTime>` are unchanged and remain a known gap. SQL Server, MySQL and Firebird keep the
default; they were not measured. No public signature or stored format changes.

## Evidence

- `IdentityValueConvergence` (adapter-surface kit): Guid `==`, `!=`, `In`, nullable `==` and `!=`, DateTime `==`, `>`,
  `<=` and a range, over UTC values with a sub-microsecond tick and an Unspecified value with no fraction. SQLite
  diverged on nine cases and DuckDB threw on three before this change; both pass after; MongoDB passes.
  `PostgresIdentityValueConvergenceSpec` compiles; it needs a server and was not run.
- `NpgsqlDocumentComparisonSpec` (Data.Relational) pins the PostgreSQL SQL and the comparand text.
