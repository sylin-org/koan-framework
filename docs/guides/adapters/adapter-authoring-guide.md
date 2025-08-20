# Sora Adapter Authoring Guide (draft)

- Families: Relational, Document, Vector (v1); optional: Search, Graph, Time-series, Event store, Columnar.
- Redis: included as a Document adapter for v1.
- Implement IDataRepository and IBatchSet; support repo pipeline hooks.
	- If the adapter supports native bulk upsert/delete, implement `IWriteCapabilities` and set `Writes` flags; optionally implement `IBulkUpsert<TKey>` / `IBulkDelete<TKey>`.
	- Ensure `UpsertManyAsync` / `DeleteManyAsync` use native bulk operations when available; otherwise provide efficient fallbacks.
	- Query capabilities: implement `IStringQueryRepository<TEntity,TKey>` for parameterized SQL or query strings; implement `ILinqQueryRepository<TEntity,TKey>` and `IQueryCapabilities` if you support LINQ predicates. For relational providers, it’s acceptable to start with in-memory filtering for LINQ (materialize then `.Where(predicate)`), but document performance trade-offs and prefer string queries for large datasets.
		- Paging & counts: expose efficient `CountAsync(..)` and prefer native paging pushdown (LIMIT/OFFSET + ORDER BY; or .Skip/.Take with sort) when your adapter can translate filters. The web layer will fall back to in-memory paging and emit `Sora-InMemory-Paging: true` if native paging isn’t available.
- Options validation, Dev-only discovery, explicit config precedence, metrics/logs.
- Transactions & batch: atomic when supported; else per-item with error aggregation.
- Security: TLS/auth config, redact secrets, warn on insecure Dev defaults.
- Tests: Testcontainers-based integration per adapter.

## Configuration and options binding
- Adapters should expose a simple options class (e.g., `SqliteOptions`, `JsonDataOptions`).
- Bind options from configuration in your module initializer using an `IConfigureOptions<TOptions>` implementation.
	- Canonical keys:
		- Per-adapter root: `Sora:Data:<adapter>` (e.g., `Sora:Data:Sqlite`, `Sora:Data:Json`).
		- Named default source: `Sora:Data:Sources:Default:<adapter>`.
		- General connection strings: `ConnectionStrings:<name>` when applicable.
- Example bindings:
	- JSON directory: `Sora:Data:Json:DirectoryPath` or `Sora:Data:Sources:Default:json:DirectoryPath`.
	- SQLite connection: `Sora:Data:Sqlite:ConnectionString`, `Sora:Data:Sources:Default:sqlite:ConnectionString`, or `ConnectionStrings:Default`.

	## Storage naming conventions
	- Centralized resolution (framework-owned):
		- The framework computes and caches names via `StorageNameRegistry.GetOrCompute<TEntity,TKey>(IServiceProvider)`.
		- Providers register an `INamingDefaultsProvider` to supply adapter defaults (style, separator, casing) and optional adapter-level overrides.
		- Precedence when deriving defaults:
			1) `[Storage(Name/Namespace)]` explicit mapping wins.
			2) `[StorageName("...")]` single-name shortcut.
			3) `[StorageNaming(Style)]` per-entity hint.
			4) Adapter defaults provided by the registered `INamingDefaultsProvider` (e.g., via `MongoOptions`, `SqliteOptions`).
	- Adapter guidance:
		- Register your `INamingDefaultsProvider` in the module initializer using `TryAddEnumerable`.
		- Consume names from `StorageNameRegistry` (avoid per-adapter `GetStorageName`).
		- Keep explicit mappings authoritative to avoid breaking changes.

	- App ergonomics:
		- A single delegate can override naming globally: `services.OverrideStorageNaming((type, conv) => string? name);`
		- Global fallback defaults can be set via `services.ConfigureGlobalNamingFallback(...)` or config `Sora:Data:Naming:*`.

## Health contributors (pull checks)
- Provide a tiny `IHealthContributor` that performs a lightweight check:
	- JSON: ensure data directory exists and is writable (create/delete a temp file).
	- SQLite: open a connection and run a trivial probe (e.g., `PRAGMA user_version`).
- Mark contributors `IsCritical = true` so readiness returns 503 when they fail.
- Auto-register your contributor in the module initializer (e.g., `*SoraInitializer.Initialize`) via `services.AddHealthContributor<T>()`.
- Keep checks fast and side-effect free; do not perform heavyweight operations.

## Provider priority and default selection
- Adapters may be discovered via `ISoraInitializer`. To influence the default provider when multiple factories are present, apply `ProviderPriorityAttribute` to your `IDataAdapterFactory` implementation. Higher values win; default is 0.
- The default provider is chosen by highest priority, then factory type name (stable, case-insensitive) when priorities tie.
- Explicit configuration (e.g., via options or explicit AddXyz) always takes precedence over discovery.

## LINQ in relational adapters (guidance)
- Minimal viable approach: materialize results (e.g., full table or coarse prefilter) and apply LINQ predicates in-memory. This is simple and correct but not efficient for large datasets.
- Optional next step: a small LINQ-to-SQL helper translating a limited subset of expressions (binary comparisons, boolean combinations, `StartsWith`/`Contains`/`EndsWith`, and simple property access) into a WHERE clause with parameters. Keep the scope intentionally small and fail fast to string-query fallback for unsupported shapes.
- Safety: always bind parameters (Dapper or ADO), never concatenate user input.

### Minimal pushdown with ILinqSqlDialect
- Implement `Sora.Data.Relational.Linq.ILinqSqlDialect` in your provider (can be on the same class as your schema dialect) to supply:
	- `QuoteIdent`, `EscapeLike`, `Parameter(int index)`.
- Use `Sora.Data.Relational.Linq.LinqWhereTranslator<TEntity>` to translate supported predicates into `WHERE` SQL plus a value list. Example wiring:
	- Build a cached `SELECT` list with `RelationalCommandCache.GetOrAddSelect<TEntity>(dialect, ...)`.
	- Try translate; on success, execute `SELECT {list} FROM {table} WHERE {where}` with Dapper and bind `@p{index}` values.
	- On `NotSupportedException`, fallback to materialize + in-memory `.Where(predicate)`.

	### Caching rendered commands
	- Provider/dialect: cache grammar-specific templates (SELECT lists, UPSERT/DELETE forms) in a small static cache.
	- Per-entity: store rendered command strings and compiled binders in the entity’s configuration bag via `AggregateBags.GetOrAdd`.
	- Use composite keys: (entity, keyType, dialect, optionsHash, version) for safe reuse.
