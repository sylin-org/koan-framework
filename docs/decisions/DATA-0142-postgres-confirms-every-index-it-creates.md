---
id: DATA-0142
slug: postgres-confirms-every-index-it-creates
domain: Data
status: Accepted
date: 2026-10-21
title: PostgreSQL confirms every index it creates, and refuses one a name collision would skip
related:
  - DATA-0141
---

# DATA-0142: PostgreSQL confirms every index it creates, and refuses one a name collision would skip

## Context

PostgreSQL index names are unique per schema and are truncated to 63 bytes, and `CREATE INDEX IF NOT EXISTS` skips with
a notice when any index of that name exists. Koan uses an explicit `[Index(Name)]` verbatim and otherwise
`IX_{table}_{leaves}` without a length limit. A declared index was therefore silently missing for every partition table
after the first that shares an explicit name, for the second of two entities sharing one, and for every index after the
first on a table whose name is long enough that the generated names truncate alike (any clamped partition table). A
missing UNIQUE index lets duplicates in. Nothing checked the index after creating it. SQLite already refuses an index
name owned by another table. Ledger NK-056.

## Decision

On PostgreSQL (not CockroachDB, which scopes index names to their table), the DDL executor:

1. refuses, before creating anything, a second declared index on one table whose name truncates to the same 63 bytes;
2. after `CREATE INDEX IF NOT EXISTS`, reads the catalog for the effective name in the schema and refuses when no index
   holds it, when it belongs to another table, when the declared index is unique and the existing one is not, or when
   the key counts differ.

The refusal is an `InvalidOperationException` naming the index, the table, the owner it found and the remedy. Index
names are not changed: shorter or partition-qualified names would create new indexes beside the existing ones on
deployed tables, which is an owner decision.

### Owner decision: index naming

- **A. Clamp generated names to 63 bytes with a hash suffix, and qualify explicit names per partition table.** Every
  declared index can exist. Existing tables gain a second copy of each index whose name changes, and a unique index can
  fail to build where duplicates already slipped in.
- **B. Keep names and refuse collisions (this decision).** Nothing on disk changes; affected entities stop at
  provisioning with a corrective message instead of running without their indexes. A partitioned entity with an
  explicit unique name, or a long-named table with two indexes, has no working configuration until A is decided.
- **C. Refuse only unique gaps and warn on the rest.** Keeps performance-only gaps running.

## Compatibility

Behavioural on PostgreSQL: an entity whose declared index was silently skipped now fails at provisioning with a
corrective message, so a deployment that ran without a unique index stops until the name is fixed. Entities whose
indexes all exist are unaffected; the check is one catalog query per index, during provisioning only. No public
signature, stored data or index changes.

## Evidence

`NpgsqlIndexNamesSpec` (Data.Relational, no server) covers truncation at a character boundary, the same-table truncation
collision, another table's ownership, a missing index, a non-unique existing index, a key-count difference and
acceptance. `PostgresIndexNameSpec` (two partitions sharing an explicit unique name) compiles; it needs a server and was
not run.
