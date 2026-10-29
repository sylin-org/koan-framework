---
id: DATA-0160
slug: sqlite-qualifies-declared-index-names-per-partition-table
domain: Data
status: Accepted
date: 2026-10-29
title: SQLite qualifies a declared index name per partition table, by the one relational rule PostgreSQL follows
related:
  - DATA-0119
  - DATA-0142
  - DATA-0146
  - DATA-0150
  - DATA-0159
  - ARCH-0096
---

# DATA-0160: SQLite qualifies a declared index name per partition table, by the one relational rule PostgreSQL follows

Amends [DATA-0146](DATA-0146-postgres-index-names-fit-63-bytes-and-are-qualified-per-partition.md): its rule 2 (a
declared name is qualified per partition table) applies to SQLite as well, and the rule moves out of the PostgreSQL
runtime into `Koan.Data.Relational`, where both stores take it from.

## Context

Measured at `943e6358a` by a consumer on SQLite whose edit and remove commands write a pre-edit copy of a record to an
`audit` partition (`Data<T,string>.Upsert(model, "audit")`) inside one `TransactionGuarantee.Atomic` scope, for an
entity that declares eight indexes by name (`[Index(Name = "ix_post_topics", ...)]`). SQLite index names are
database-wide. The relational orchestrator planned the declared name as written for every container, so the entity's
own table took it and the partition table's provisioning was refused:

```
InvalidOperationException: Index 'ix_post_topics' for main/<namespace>.Post#audit is already owned by SQLite table
'<namespace>.Post'. SQLite index names are database-wide; choose a name unique across containers.
```

The smallest reproducer needs no transaction:

```csharp
public sealed class Note : Entity<Note> { [Index(Name = "ix_note_topic")] public string Topic { get; set; } = ""; }
await new Note { Id = "a", Topic = "t" }.Save(ct);
await Data<Note, string>.Upsert(new Note { Id = "a#v1", Topic = "t" }, "audit", ct);   // refused
```

PostgreSQL had the same defect and DATA-0146 fixed it there alone, inside `NpgsqlIndexNames`. On SQLite an entity that
declares an index name had no working partition at all, whichever container was provisioned first: the other one was
refused. Names derived by Koan (`IX_<table>_<members>`) were never affected, because they carry the table, which carries
the partition.

## Decision

1. **One rule, owned by the relational pillar.** `RelationalIndexNames.Name(table, index, partitioned, maximumBytes)`
   (`Koan.Data.Relational.Orchestration`) is the name an index is created under on a store whose index names span its
   containers. A derived name is fitted to the store's limit with the identifier engine (ARCH-0096). A declared name is
   used as written on the entity's own table and, on a partition table, becomes `<name>_<hash>`, the hash being the
   first eight lower-case hex characters of the SHA-256 of the table name, so one declaration yields one index per
   container. PostgreSQL binds it under its 63-byte limit (`NpgsqlIndexNames.Name`, whose names do not change); SQLite
   binds it with no limit. The rule is a decision rather than grammar (DATA-0119), it is identical for both stores, and
   two copies of it — hash, separator, which names are qualified — would drift. It stays at the executor's call rather
   than in the orchestrator's plan because only the executor knows which container it speaks to: the repository that
   resolved the ambient partition constructs it per container, the shape DATA-0146 already set.
2. **SQLite applies it to every index it keys by name.** `SqliteDdlExecutor` is told by the repository whether the
   table is a partition of its entity (an ambient partition selects the table) and creates, inspects, replaces and finds
   every keyed index under the rule's name. `FindIndex`, which DATA-0150 uses to report the global unique index an
   earlier build left behind, looks for the name that index would have had on that table. The lexical index of
   DATA-0159 is untouched: its view, FTS5 table and triggers are named from the table (`<table>__koan_lexical...`), so
   each container already has its own, and a partition of an entity that declares both `[Lexical]` and named indexes
   provisions and searches its own rows.
3. **The refusal stays where a collision is genuine.** Two different entities that declare one name still meet on
   their own tables and the second is refused, naming the owner and the remedy. (Their partition tables do not meet:
   each takes a copy qualified by its own table name.) A partition table that holds the unqualified declared name
   itself — possible only in a database whose partition table was provisioned before its entity's own table, before
   this record — blocks the entity's own table the same way; the refusal says so and gives the `DROP INDEX` statement.
4. **Existing databases gain names; nothing is dropped.** The entity's own table keeps its declared names. A partition
   table provisioned from now on gets the qualified copies. A partition table that already held an unqualified declared
   name gains its qualified copy beside it; the old index is then redundant. List a table's indexes with
   `SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = '<table>'` and drop a redundant one with
   `DROP INDEX "<name>";`.

## Compatibility

- Behavioural on SQLite: a partition table of an entity that declares an index name now provisions, with its own copy
  of each declared index; it was refused before. The entity's own table, every derived name and every entity without a
  declared name are unchanged.
- PostgreSQL: unchanged. Its rule is now the shared one with its own limit, producing the same names.
- `RelationalIndexNames` is a new public type in `Koan.Data.Relational`. `SqliteDdlExecutor` (internal) gains a
  defaulted constructor parameter. No other signature, stored format or package dependency changes.
- Partition names that differ only by letter case (`Audit`, `audit`) resolve to one SQLite table, because SQLite
  compares identifiers without case, and the two spellings hash differently, so that table holds two copies of each
  declared index. The two partitions already share their rows there (the case-folding residual of DATA-0077); this
  record does not change that.

## Not covered

- DuckDB's executor refuses an index name another table owns with the same rule SQLite had (`DuckDbDdlExecutor`,
  "DuckDB index names are schema-wide"), so a DuckDB partition of an entity that declares an index name is still refused.
  Applying this rule there is a change of its own, with its own proof.

## Evidence

- `SqlitePartitionIndexNameSpec` (Connector.Sqlite): the reproducer saves; a partition copy of an entity with four
  declared names (one composite) and a derived one commits inside one Atomic scope with the change it records; the
  entity's own table holds exactly the declared names and each of two partition tables holds `<name>_<hash of its
  table>` for each, with the derived name unqualified, and the planner reads the partition through its copy; two
  entities that declare one name are still refused on their own tables, naming the owner; a partition table holding
  the unqualified name blocks the entity's own table with the `DROP INDEX` statement, and after that statement the
  entity's own table provisions; a partition of an entity with a lexical index and a named index searches only its own
  rows. Five of the six failed before this change; the refusal of two entities passed before and after.
- `RelationalIndexNamesSpec` (Data.Relational, no store): the declared, qualified, derived, unlimited and 63-byte forms.
- `NpgsqlIndexNamesSpec` and `PostgresIndexNameSpec` (container) pass unchanged against the shared rule, so PostgreSQL's
  names did not move.
