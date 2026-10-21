---
id: DATA-0146
slug: postgres-index-names-fit-63-bytes-and-are-qualified-per-partition
domain: Data
status: Accepted
date: 2026-10-21
title: PostgreSQL index names fit 63 bytes without collisions, and a declared name is qualified per partition table
related:
  - DATA-0142
  - DATA-0144
---

# DATA-0146: PostgreSQL index names fit 63 bytes without collisions, and a declared name is qualified per partition table

## Context

DATA-0142 made PostgreSQL confirm every index it creates, because index names are unique per schema, are cut to 63 bytes
and are skipped by `CREATE INDEX IF NOT EXISTS` on any name match. It left the names unchanged and refused the
collisions, so two configurations had no working form: an entity with an explicitly named unique index written in more
than one partition (every partition table after the first collided with the first's index), and a table whose name is
long enough that its derived index names are cut alike (any clamped partition table with two indexes). Ledger NK-056.

## Decision

Owner decision, option A of DATA-0142. PostgreSQL index names are composed by the shared identifier engine (ARCH-0096)
under a 63-byte policy:

1. **Derived names fit without being cut.** A derived name (`IX_<table>_<members>`) is unchanged while it is 63 bytes or
   fewer. A longer one keeps a readable prefix and ends in `_` plus an eight-character hash of the whole name, the same
   rule table names already follow, so names that used to be cut alike stay distinct. A derived name already carries its
   table, which already carries its partition.
2. **A declared name is qualified per partition table.** `[Index(Name = ...)]` is used as written on the entity's own
   table. On a partition table (an ambient partition selects the table) it becomes `<name>_<hash of the table name>`
   (eight hex characters, clamped to 63 bytes by the same rule), so one declaration yields one index per partition table
   and each unique index enforces uniqueness in its own table.
3. **The catalog verification of DATA-0142 stays.** Two entities that declare the same name in one schema still collide
   on their own tables and are refused with the message that names the owner; two declared names on one table that still
   truncate alike are refused.
4. **Existing databases gain the new indexes.** Nothing is dropped or renamed. A table that already holds the old name
   gets the new name beside it. The old index is then redundant, and for a partition table the old unqualified name was
   only ever held by the first partition table. Drop it by hand once the new one exists:
   `DROP INDEX "<schema>"."<old name>"` (list candidates with
   `SELECT indexname, indexdef FROM pg_indexes WHERE tablename = '<table>'`). A unique index that could not be created
   before because duplicates had slipped in fails to build; remove the duplicates first.
5. **CockroachDB does not change.** It shares the executor but scopes index names to their table and does not cut them,
   so the defect does not exist there and renaming would only add duplicate indexes. The qualification applies where the
   store reports schema-wide names (`IndexNamesAreSchemaWide`, the same switch as the verification).

The mapping plan records whether a name was declared (`MappingIndexPlan.NameDeclared`), carried to
`RelationalIndexDefinition.NameDeclared`, because only a declared name can be shared by several tables.

## Compatibility

Behavioural on PostgreSQL:

- An entity that declares an index name and is written in a partition gets, for the partition table, an index under the
  qualified name in addition to any old one. The base set's declared names are unchanged.
- A derived name longer than 63 bytes changes (it was being cut). Names that fit are unchanged, so most databases gain
  nothing.
- A configuration DATA-0142 refused (a shared declared name across partitions, two long derived names on one table) now
  provisions.
- No public signature changes; `MappingIndexPlan` and `RelationalIndexDefinition` gain a read-only property.

## Evidence

`NpgsqlIndexNamesSpec` (Data.Relational, no server) covers fitting, determinism, the 63-byte bound and per-table
qualification. `PostgresIndexNameSpec` (Connector.Postgres, PostgreSQL 18.6) covers: a declared unique name yielding one
unique index per partition table, each enforcing uniqueness in its own table; two indexes on a clamped partition table
staying distinct; a table that holds the old unqualified name gaining the qualified index while the old one stays; and two
entities sharing a declared name still being refused. Three of the four failed before this change.
