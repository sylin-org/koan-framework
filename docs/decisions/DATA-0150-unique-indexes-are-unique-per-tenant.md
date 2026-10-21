---
id: DATA-0150
slug: unique-indexes-are-unique-per-tenant
domain: Data
status: Accepted
date: 2026-10-21
title: A unique [Index] on a row-scoped entity is unique per tenant
related:
  - DATA-0144
  - DATA-0142
  - DATA-0145
  - DATA-0146
  - ARCH-0099
  - ARCH-0096
---

# DATA-0150: A unique `[Index]` on a row-scoped entity is unique per tenant

Accepts option A of [DATA-0144](DATA-0144-unique-indexes-under-row-scoped-tenancy.md), which stays a dated record of the
options.

## Context

Row-scoped tenancy keeps the tenant in a managed field inside the document, not in a CLR property, and index keys were
built from CLR properties only. `[Index(Unique = true)] Email` was therefore unique across every tenant: tenant B could
not register an address tenant A held, the duplicate-key error told B that some other tenant held it, and the
application could not add the tenant to the index because it is not a property. Ledger NK-039.

## Decision

Owner decision, 2026-10-21: option A of DATA-0144, **per tenant by default**.

1. **Every unique index leads with the row-scoped managed fields.** When the host's segmentation plan scopes the entity
   (`DataSegmentationPlan.For(type).Fields`, today the `tenant` dimension that `Koan.Tenancy` contributes, stored as
   `__koan_tenant`), every unique, non-primary index leads with those fields, read the way every filter reads them:
   - relational stores, through the shared schema orchestrator: each managed field is a part inside the document
     root, rendered by the dialect's own read, for every store whose features declare
     `SupportsManagedFieldIndexParts` (SQLite, and PostgreSQL and CockroachDB through the shared Npgsql adapter). PostgreSQL builds `(("Json" #>> '{__koan_tenant}'), ("Json" #>> '{Email}'))`; SQLite leads
     with the `json_extract` of `__koan_tenant` from the `Json` column that its tenant filter emits;
   - MongoDB keys it `{ __koan_tenant: 1, email: 1 }`, with DATA-0145's field names.
   Non-unique indexes, primary keys and entities the plan does not scope (`[HostScoped]`, no tenancy) are unchanged.
   MySQL and SQL Server index a document value only through the projected column they compute per mapped property,
   and have no such column for a managed field; DuckDB and Firebird build no expression indexes. They keep the global
   unique index they always built and report `TenantScope:<index>` as an unproved claim in the schema validation.
2. **A scoped index has its own name**, so an existing database keeps the old one and gains the new one beside it. A
   derived name gains the managed fields as its leading members (`IX_Member___koan_tenant_Email`); a declared name gains
   them as a suffix (`ux_member_email___koan_tenant`). On MongoDB an unnamed declaration takes MongoDB's own name for its
   keys (`__koan_tenant_1_email_1`). Relational scoped names are fitted to 63 bytes by the shared identifier engine
   (ARCH-0096), so a name that fitted MySQL's or PostgreSQL's limit before still fits. PostgreSQL then applies
   DATA-0146 unchanged: a declared name is qualified per partition table and the catalog verification of DATA-0142
   confirms the scoped index under its own name and key count.
3. **Koan never drops an index.** After building a scoped index Koan looks for the global one an earlier version built
   for the same declaration (`IRelationalDdlExecutor.FindIndex`, implemented by SQLite and PostgreSQL; MongoDB lists the
   collection's indexes). Whenever it is there, on every start that provisions the entity, a warning names it, says it still refuses for every tenant a value one
   tenant holds, and gives the statement that drops it.
4. **A duplicate is a conflict that says nothing about where the value is held.** The facade reports a store's
   unique-index violation as `UniqueValueConflictException` (`Koan.Data.Abstractions.Failures`), whose `Failure` is
   `DataFailureKind.Conflict` and never replayed. Each adapter classifies its own error through `IDataFailureClassifier`
   with the stable code `unique-value`: SQLite extended code 2067, PostgreSQL SQLSTATE 23505, MongoDB duplicate key.
   The store's error is not attached, because it names the index that refused the value: while an old global index
   exists, that name would tell the caller whether another tenant holds it. Every facade write verb translates (Save,
   Save of many, Insert, UpsertWithOutcome, ReplaceIf, Batch).

## Migration of an existing deployment

Nothing is lost at any step: a duplicate inside one tenant is refused throughout, and nothing the old index refused is
accepted until the operator drops it.

1. **Build the new index.** Upgrade and start the application. On first use of each scoped entity Koan creates the
   per-tenant index beside the global one and logs a warning naming the global index and the statement that drops it.
2. **Verify it.** Confirm the new index exists: SQLite
   `SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = '<table>'`; PostgreSQL
   `SELECT indexname, indexdef FROM pg_indexes WHERE tablename = '<table>'`; MongoDB
   `db.getCollection("<collection>").getIndexes()`. Building it cannot fail on existing rows: they already satisfy the
   stricter global index.
3. **Drop the old one** with the statement from the warning: SQLite `DROP INDEX "<old>";`, PostgreSQL
   `DROP INDEX "<schema>"."<old>";`, MongoDB `db.getCollection("<collection>").dropIndex("<old>")`. From then on one
   value is accepted in two tenants.

Until step 3, uniqueness stays global, exactly as before the upgrade. CockroachDB builds the scoped index but does not
look for the old one, so it logs nothing; find it with `SHOW INDEXES FROM <table>` and drop it the same way.

## Compatibility

- No working application fails at boot and no data is lost. The scoped index has a new name, so it never collides with
  the old one, and the old one is kept; building the scoped index over rows that satisfied the global one cannot fail.
- A deployment keeps global uniqueness until the operator drops the old index; a new database is per tenant at once.
- An application that catches the native driver exception for a duplicate (`SqliteException`, `PostgresException`,
  `MongoWriteException`) now receives `UniqueValueConflictException`, with no inner exception. This is the one source
  change, and it is what keeps the error from leaking across tenants.
- A store that cannot key a managed field keeps the index it always built, so nothing about it changes but the
  unproved claim, which is reported as unverified and never stops a boot.
- `RelationalIndexDefinition` gains `Unscoped`, `IRelationalDdlExecutor` gains `FindIndex` with a default that finds
  nothing, and `IRelationalStoreFeatures` gains `SupportsManagedFieldIndexParts` defaulting to false, so existing
  executors and features compile unchanged.

## Not covered

- Isolation axes declared through `ManagedFieldRegistry` (a Shared-mode data axis such as an organisation) are not part
  of the segmentation plan and do not scope unique indexes. Whether they should is a separate decision.
- Explicit relational maps and explicit MongoDB maps refuse managed fields already, so they have nothing to scope.
- MySQL, SQL Server, DuckDB and Firebird keep global uniqueness (rule 1). Giving MySQL and SQL Server a projected
  column for the managed field would change the shape of every existing scoped table, which their `Validate` and
  `NoDdl` policies would then report as drift; that needs its own decision.
- Only SQLite, PostgreSQL and MongoDB classify a duplicate; the other adapters still surface their driver's error.
- CockroachDB was compiled, not run.

## Evidence

- `UniqueIndexTenancySpecsBase` (Data adapter-surface kit) with `SqliteUniqueIndexTenancySpec`,
  `PostgresUniqueIndexTenancySpec` (PostgreSQL 18.6) and `MongoUniqueIndexTenancySpec` (MongoDB 8.3), under a row-scoped
  `tenant` dimension contributed exactly as `Koan.Tenancy` contributes it: one value accepted in two tenants and a
  duplicate in one tenant refused as a conflict that names neither tenant nor index; and an existing deployment whose
  index was built global gaining the per-tenant index, keeping both guarantees while the warning stands, and accepting
  the value in a second tenant once the reported index is dropped. All six fail with the scoping and the translation
  disabled and pass with them.
- `TenantUniqueIndexSpec` (Tenancy, SQLite) proves the same through the real `Koan.Tenancy` module.
- Six existing specs that asserted the driver's duplicate-key exception (`SqliteInsertOnlySpec`, `PostgresInsertOnlySpec`,
  `MongoInsertOnlySpec`, `MongoAcronymIndexSpec`, two in `MongoExistingIndexSpec`) now assert
  `UniqueValueConflictException`; what they prove is unchanged (the write is refused, and on insert it is a failure,
  not an identity conflict).
