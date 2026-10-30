# Sylin.Koan.Data.Relational technical notes

## Ownership

`RelationalModule` is the one functional owner of relational mapping consumption and schema orchestration. Concrete
providers reference this package and receive those services through normal Koan composition; they do not register
bridges or mutate shared relational options.

Every repository passes the already-resolved table plus an immutable `RelationalSchemaPolicy` to the orchestrator.
That policy carries projection mode, DDL policy, matching strictness, production guard, and provider schema. This makes
schema decisions local to the selected provider/source route and prevents one connector from changing another.

Provider implementations supply `IRelationalDdlExecutor`, `IRelationalStoreFeatures`, and `ILinqSqlDialect` from the
module-free Abstractions package. Application code should not consume these contracts.

## Mapping and commands

- `RelationalCommandPlanner` accepts one compiled `MappingPlan`. It emits symbolic physical reads, encoded values,
  identity predicates, conditions, filters, ordering, query intent, and the source mapping receipt; it emits no SQL.
- Insert/update/delete/get/patch/conditional/query and materialization all use `MappingBindingPlan`. Adapters lower the
  returned `PhysicalPath`; they never parse CLR expressions or rebuild naming/codec decisions.
- `SqlFilterTranslator(IRelationalMappingDialect, MappingPlan)` resolves the filter through the map and applies the
  binding codec before comparable scalar encoding. The caller-resolver constructor remains only for adapters awaiting
  their own conformance card.
- `RelationalPlanCache<TPlan>` holds a repository's compiled plans, one per physical container, as an LRU: a full cache
  drops the least recently used plan and never refuses a container (DATA-0162). Every relational adapter keys its plans
  through it.
- `In` and `Nin` lower through `ILinqSqlDialect.MemberOf` first: a dialect that binds the whole set as one value (a
  PostgreSQL array through `SqlArrayParameter`, a JSON array read by SQLite's `json_each`, a SQL Server table-valued
  parameter), each member encoded as the store binds it alone, carries a set of any size. Otherwise each member is its
  own parameter, in IN lists of at most `MaximumInListMembers`. The parameter bound belongs to the statement
  (`MaximumParameters`, the store's measured limit, less the parameters the statement binds itself, which its caller
  passes to `Translate(filter, reservedParameters)`): while a statement is over it, the largest remaining per-member
  list moves to one value and the filter is lowered again. Every list-valued operator is such a list: `In`/`Nin`
  through `MemberOf`, and `HasAny`/`HasAll`/`HasNone` through `CollectionMemberOf`, which binds wherever the dialect
  carries the set. A dialect that `PrefersSetParameters` keeps `In`/`Nin` as lists until the statement is over. A
  statement still over with nothing more that can move is refused with a `NotSupportedException` naming the lists that
  could not and why (`OneValueRefusal`), or saying the rest are plain comparisons, never sent to a store that would
  reject it. `Has` and the list operators compare a JSON array element alike: the comparand in the canonical store
  form, then the dialect's `JsonArrayComparand`, against the element as `JsonArrayContains(column, parameter,
  elementType)` reads it. A `Not` over a field's `In` lowers as its `Nin`, and the reverse (DATA-0162).
- A parameter value implementing `ISqlParameterValue` configures its own provider parameter (`SqlArrayParameter`, SQL
  Server's table-valued sets); `SqlParameters` binds it as one value instead of expanding a sequence.
- `AdoCommands.QueryRowsAsync(…, maxRows, cancelAtBound)` reads at most `maxRows` rows. With `cancelAtBound`, which
  the adapters pass only for a declared Read (`BoundedReads.MayCancel`), it cancels the command at the bound before the
  reader closes, so a driver that drains on close does not read the rest; otherwise the reader drains to the end and
  every statement the command holds completes (DATA-0162).
- A store whose sets bind one parameter per member declares the most a statement binds as
  `FilterSupport.MaximumSetMembers`; its own `GetMany` and `DeleteMany` name ids in slices of that size, deleting the
  slices inside one transaction (DATA-0162).
- The Family owns no provider connection, SDK type, SQL dialect, native error code, or resource lifetime.

## Schema behavior

- `NoDdl`: inspect only; missing schema is reported and creation is rejected.
- `Validate`: inspect only; callers can use the report as a readiness or corrective signal.
- `AutoCreate`: create a missing table and add missing projected columns when the environment guard allows it.
- `Relaxed`: a mismatch reports `Degraded`.
- `Strict`: a mismatch reports `Unhealthy`; repositories surface their corrective schema error.
- Definition validation compares physical type, nullability, scalar/structured shape, identity, and generation. A
  same-named incompatible value is unhealthy; name existence alone is at most degraded/unverified.
- `External`: validation is allowed, but create/add/index/TTL mutation is rejected before the DDL executor.

Creation is additive. Koan does not rename/drop columns, infer destructive migrations, or promise full migration
management. Production DDL remains denied unless the selected provider explicitly allows it.

Index plans carry the same binding and encoding identities as filter/write plans. Nested expression indexes,
rewrite-free behavior, and TTL are executed only when `IRelationalStoreFeatures` proves the matching native profile;
unsupported TTL metadata is never lowered as an ordinary index.

A store whose index names span its containers (PostgreSQL schemas, SQLite databases) creates each index under
`RelationalIndexNames.Name`, the one rule for such stores (DATA-0146, DATA-0160): a derived name, which carries its
table, is fitted to the store's limit; a declared `[Index(Name = ...)]` is used as written on the entity's own table and
qualified by a hash of the table name on a partition table, so one declaration yields one index per container. The plan
keeps the declared name; the executor applies the rule, because the repository builds it per container and so it alone
knows whether its table is a partition. PostgreSQL passes its 63-byte limit, SQLite none; CockroachDB, whose names are
per table, keeps the declared names.

Under row-scoped segmentation, on a store whose features declare `SupportsManagedFieldIndexParts` (SQLite, Npgsql), the
orchestrator leads every unique, non-primary index with the managed fields (`__koan_tenant`) read inside the document
root, under a name of its own fitted to 63 bytes, and keeps the global
predecessor in `RelationalIndexDefinition.Unscoped`; `RelationalPlanGuard` accepts exactly that shape. After creating a
scoped index it asks `IRelationalDdlExecutor.FindIndex` for the predecessor and logs a warning with its drop statement
when the store holds it; it never drops it. SQLite and PostgreSQL implement `FindIndex`; the default finds nothing.
Any other store keeps the global unique index and reports `TenantScope:<index>` as unproved (DATA-0150).

## Query translation

The common translator supports scalar equality/comparison, logical composition, null checks, Boolean members, and the
basic string operations providers can represent safely. Provider dialects own quoting, parameters, LIKE escaping, and
JSON-array operations. Unsupported expressions fail closed; Koan does not silently scan an unbounded source.

Provider-specific SQL, connection lifecycle, discovery, health, and startup reporting remain in each concrete connector.

## Enum storage contract

Default Entity storage preserves enum names as strings, including nullable values, nested values, collections,
and declared EnumMember aliases. Unnamed numeric values fail instead of silently changing the storage format.
Queries use the same spelling. Ordinary enum ordering uses declared ordinal ranks in native expressions while
the stored value stays a string; native ordering of arbitrary Flags combinations rejects correctively.
An explicit external mapping codec remains responsible for its declared physical representation.
Existing numeric rows or columns require a separate, explicit migration; upgrades do not rewrite them automatically.
