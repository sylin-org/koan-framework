---
id: DATA-0135
slug: sqlite-string-matching-is-case-sensitive
domain: Data
status: Accepted
date: 2026-10-21
title: Scalar StartsWith, EndsWith and Contains are case-sensitive on SQLite, through a dialect string-match hook
related:
  - DATA-0134
  - ARCH-0079
---

# DATA-0135: Scalar StartsWith, EndsWith and Contains are case-sensitive on SQLite, through a dialect string-match hook

## Context

The oracle compares strings ordinally unless a filter asks for `IgnoreCase`, and `RelationalFilterSupport` declares
`IgnoreCase = false`, so the splitter pushes only case-sensitive string operators to relational stores. The shared
translator lowered scalar `StartsWith`, `EndsWith` and `Contains` to `col LIKE @p ESCAPE '\'` on every dialect.
SQLite's `LIKE` folds ASCII case. `SqliteDialect` already knew this and matched collection elements with `instr`, but
the scalar path had no seam to do the same.

Measured on SQLite before this change with the shared corpus: `{"Name":"al*"}` returned `[w1,w4]` (Alpha, Alfred)
where the oracle returns nothing; `*HA` and `*LPH*` returned `[w1]` against `[]`. Ledger NK-022 and NK-032. The answer
also changed within SQLite depending on whether the predicate was pushed down or evaluated as a residual.

## Decision

1. `ILinqSqlDialect.StringMatch(column, op, value, bind)` is a new default member. The default is the existing LIKE
   form over the `EscapeLike`-escaped pattern, so PostgreSQL, DuckDB, MySQL, SQL Server and Firebird emit what they
   emitted before. `bind` binds one value and returns its placeholder, so a dialect binds only what it uses.
2. SQLite binds the raw literal and compares exactly: `substr(col, 1, length(@p)) = @p` for StartsWith,
   `substr(col, length(col) - length(@p) + 1) = @p` for EndsWith, `instr(col, @p) > 0` for Contains. Function results
   carry no collation, so `=` is binary even over a column declared `NOCASE`. Wildcard characters in the value are
   literal. A null value answers null, which filters as false, as the oracle does.

## Compatibility

Behavioural, SQLite only: a scalar string match in the wrong case no longer matches (`Title.StartsWith("ship")` stops
returning "Ship it"). Applications that relied on the folding must ask for it with a case-insensitive filter, which
the framework evaluates. The SQLite predicate does not use an index on the column; the LIKE form it replaces could use
one only under `case_sensitive_like`, which Koan never set. No signature or stored format changes.

## Evidence

- `FilterConvergence.Cases()` gains `prefix-case-mismatch`, `suffix-case-mismatch`, `contains-case-mismatch`,
  `contains-case-match` and `suffix-case-match`. `SqliteFilterConvergenceSpec` failed on the three mismatches before
  this change and passes after; DuckDB passes; MongoDB runs them against a local server; the other connectors inherit
  them, compiled, not run.
- `SqliteStringMatchSpec` (Connector.Sqlite) compares nine patterns, including `_`, `%`, a backslash, an empty value
  and a null, with the oracle; three failed before.
