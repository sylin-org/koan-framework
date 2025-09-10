# Building Data Adapters

A practical, capability‑honest path to implement a Sora data adapter (relational, document, key/value).

Contract (minimal viable surface)
- Query: advertise capabilities via IQueryCapabilities (Linq, String, PagingPushdown, FilterPushdown).
- Writes: implement Upsert/UpsertMany, Delete/DeleteMany/DeleteAll; IBatchSet with RequireAtomic honoring.
- Instructions: IInstructionExecutor with schema/data helpers appropriate to the store.
- Options: AdapterOptions (connection, default page sizes, DdlPolicy, MatchingMode, etc.).

Steps
1) Package and DI
   - Create a provider package under src/Sora.Data.<Adapter>/.
   - Add extension Add<Adapter>Adapter(IServiceCollection, Action<AdapterOptions>?).
   - Register StorageNameRegistry, options binding, capability reporter, and repository.

2) Naming and routing
   - Use StorageNameRegistry for set/table/collection names. Support INamingDefaultsProvider and attributes for overrides.

3) Query pipeline
   - LINQ: translate supported predicates; push down paging and count; fall back to in‑memory only when bounded (DATA-0032).
   - String query: accept WHERE suffix or full native SELECT/query when applicable.

4) Projection and schema
   - Relational: computed columns/indexes for projected properties (DATA-0045); DDL policy and matching mode (DATA-0046).
   - Document/KV: ensure/create collection/bucket; avoid heavy migrations; expose diagnostics via validate instruction.

5) Guardrails and options
   - Enforce DefaultPageSize and MaxPageSize. Cap page sizes and signal fallbacks where applicable.

6) Instructions
   - Common: data.ensureCreated, data.clear.
   - Relational: relational.schema.validate, relational.schema.ensureCreated; relational.sql.scalar/nonquery/query.

7) Observability
   - Structured LoggerMessage events; ActivitySource spans with db.system, db.statement (sanitized), and outcome tags.

8) Tests
   - Options/guardrails unit tests; repository happy‑path CRUD; Testcontainers integration with env opt‑in and Docker probing.

References and templates
- Acceptance criteria: support/data-adapter-acceptance-criteria.md
- Template: support/data-adapter-template.md
- Vector contracts (if applicable): guides/adapters/vector-search.md, support/vector-adapter-acceptance-criteria.md
