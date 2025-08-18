# Adapter Authoring Checklist

Use this when creating a new provider adapter.

- Package placement and naming: `Sora.Data.<Provider>`
- Implement `IDataRepository<TEntity,TKey>` and opt into capabilities:
  - `IStringQueryRepository<TEntity,TKey>` if you support string queries.
  - `ILinqQueryRepository<TEntity,TKey>` if you support LINQ predicates; also implement `IQueryCapabilities` and return `QueryCapabilities.Linq` (and `String` if applicable).
  - `IInstructionExecutor<TEntity>` for instruction passthrough.
  - Implement `IWriteCapabilities` and set `WriteCapabilities` flags (`BulkUpsert`, `BulkDelete`, `AtomicBatch` if supported). Optionally implement `IBulkUpsert<TKey>`/`IBulkDelete<TKey>` markers.
- Relational adapters
  - Place dialects inside the provider package (SoC).
  - Use the Relational toolkit for schema modeling and ensureCreated.
  - Map complex CLR types to JSON columns and hydrate on read.
- Indexes/Identifiers
  - Honor Identifier → PK/unique.
  - Deduplicate explicit Id-only Index attributes.
- Safety
  - Bind parameters (e.g., Dapper) — avoid string interpolation.
  - If LINQ is implemented via in-memory filtering, document performance expectations and prefer string queries for large datasets.
- Tests
  - CRUD happy path + complex property hydration.
  - Instruction tests: ensureCreated, nonquery, scalar.
  - Capability tests: verify `IQueryCapabilities` and `IWriteCapabilities` advertise correct flags; exercise LINQ predicate path if implemented.
  - Naming: verify provider registers an `INamingDefaultsProvider` and repository uses `StorageNameRegistry` for table/collection names.
  
- Health
  - Provide a tiny `IHealthContributor` that performs a lightweight pull check:
    - JSON: verify data directory exists and is writable (probe file create/delete).
    - SQLite: open a connection and run a trivial PRAGMA; if file-based, ensure directory exists.
  - Auto-register the contributor in your module initializer (e.g., `*SoraInitializer.Initialize`) so apps get it by default when the adapter is discovered.
