# EntityController Surface — Per-Adapter Matrix

End-to-end HTTP tests that exercise the full `EntityController<T>` surface against each Koan data adapter. Validates the DATA-0092 / DATA-0093 sort contract holds across backends, including the original-bug scenario (`?sort=-Sightings.LastChangedAt` with pagination on a complex entity).

## Architecture

- **`Koan.Web.AdapterSurface.TestKit`** — shared library. Defines:
  - `Widget` + `Sighting` (entity with scalar + nested collection, `[StorageName("widgets_surface")]`)
  - `WidgetController` (`EntityController<Widget>` on `/api/widgets`)
  - `IAdapterTestFactory` (per-adapter contract: client, services, reset, availability)
  - `AdapterSurfaceSpecsBase<TFactory>` — 21 abstract test methods covering:
    - List GET (empty, populated)
    - Sort grammar (`-` desc, `+` asc, no-prefix asc, multi-field)
    - **Deep-path collection sort** (`-Sightings.LastChangedAt`) — the original bug
    - **Deep-path sort + pagination** across all pages — the regression test
    - Sort-after-paginate regression (page 1 globally smallest, not first-inserted)
    - 400 on unresolvable sort field
    - Pagination headers (X-Page, X-Page-Size)
    - GET by id (existing, 404)
    - POST upsert (create, update)
    - POST bulk upsert
    - DELETE by id
    - POST `/query` body-sort
    - POST `/query` body deep-path + pagination
    - POST `/query` unresolvable sort → 400
    - Full round-trip regression assertion
  - `Containers/MongoContainerHelper` — Testcontainers boot for Mongo
  - `Containers/PostgresContainerHelper` — Testcontainers boot for Postgres
  - `Containers/RedisContainerHelper` — Testcontainers boot for Redis
  - `Containers/SqlServerContainerHelper` — Testcontainers boot for SQL Server
  - `Containers/CouchbaseContainerHelper` — Testcontainers boot for Couchbase (currently a clean-skip placeholder; see Status)

- **Per-adapter projects** subclass the base + bind a factory.

## Status

| Adapter | Status | Tests | Notes |
|---|---|---|---|
| **InMemory** | Validated | **21/21 passing** | No container required |
| **Json** | Validated | **21/21 passing** | File-based, temp directory |
| **Sqlite** | Validated | **21/21 passing** | File-based, temp directory; per-test reset via `DROP TABLE` enumeration |
| **Mongo** | Validated | **21/21 passing** | Testcontainer `mongo:7`, gracefully skips without Docker. **The user-reported bug scenario, verified end-to-end against a real document database.** |
| **Postgres** | Validated | **21/21 passing** | Testcontainer `postgres:16-alpine` via `Testcontainers.PostgreSql 4.11.0` |
| **Redis** | Validated | **21/21 passing** | Testcontainer Redis via `Testcontainers.Redis 4.11.0` |
| **SqlServer** | Validated | **21/21 passing** | Testcontainer via `Testcontainers.MsSql 4.11.0` |
| **Couchbase** | Cleanly skipped | 21 skipped | `Testcontainers.Couchbase` exposes KV and management on separate random host ports, but `CouchbaseClusterProvider` derives the management URL from the single KV connection string. Skips with a clear reason; runnable against an externally provisioned cluster by setting `Koan_TESTS_COUCHBASE`. Tracked as a separate framework follow-up. |

**Coverage today: 126 tests passing across 7 adapters covering the full EntityController surface.** The DATA-0092 contract is validated end-to-end against in-memory, file-based, document, relational (Sqlite/Postgres/SqlServer), and KV (Redis) backends — proving:
- The orchestrator's structured sort contract works
- The in-memory sort fallback for adapters that can't push deep-path down works
- The original-bug scenario (`?sort=-Sightings.LastChangedAt` with pagination on Mongo) is correctly fixed

## Bugs caught and fixed during this matrix build

1. **Mongo's `Skip(0).Limit(int.MaxValue)`** (shared `QueryExtensions.ApplyPaging`) silently dropped result sets. Fixed by skipping pagination application entirely when no pagination is requested.
2. **`Data<T,K>.QueryWithCount` pagination-flag semantics** — when the orchestrator paginates in memory after refetching unpaginated (the sort-not-pushed-down fallback path), it now sets `RepositoryHandledPagination = true`. The flag's downstream contract with `EntityEndpointService` is "Items is already a page; don't paginate again." Previously the orchestrator only set this flag true when the *repository* handled pagination, causing the web layer to paginate-again the already-paginated window — yielding empty results for `page > 1` on every adapter that couldn't push sort down.
3. **Postgres + SqlServer `KoanAutoRegistrar` missing `services.AddRelationalOrchestration()`** — Sqlite's auto-registrar had it but the other two relational adapters didn't, so `IRelationalSchemaOrchestrator` was unresolvable when the connector tried to provision tables under `WebApplicationFactory`-style hosting. Fixed both.
4. **DDL executors had fire-and-forget async** — `MsSqlDdlExecutor`, `PgDdlExecutor`, and `SqliteDdlExecutor` all used `ExecuteScalarAsync()` / `ExecuteNonQueryAsync()` without awaiting. `TableExists` always returned `true` because `Task<object?>` is itself not null, so `CreateTable` never ran and every adapter spec failed with "no such table" / "Invalid object name". Converted all DDL ops to their sync equivalents (DDL is bounded and synchronous by nature). This single fix unblocked Postgres, SqlServer, and Sqlite simultaneously.

## Adding a new adapter

1. Copy a working adapter project as a template (`Json` for file-based, `Mongo` for container-based).
2. Rename namespace, project, csproj refs.
3. Update `Program.cs` to set `Koan:Data:Sources:Default:Adapter` to the new adapter name.
4. Update the `Factory` class:
   - Container-based: instantiate the matching `*ContainerHelper`, inject `ConnectionString` via `ConfigureAppConfiguration`.
   - File-based: pick a temp path; clean up in `DisposeAsync`.
   - Implement `ResetAsync()` — drop the database / clear the store.
5. Add `WebApplicationFactoryContentRoot` assembly attribute (workaround for `tests/Directory.Build.props` redirecting bin to `%TEMP%`).
6. Spec class is one-liner: `public sealed class XAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<XAdapterFactory> { ... }`.

## Running

```bash
# All adapters that don't need Docker
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.InMemory.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Json.Tests

# Adapters that need Docker; tests gracefully Skip without it
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Mongo.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Postgres.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Redis.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.SqlServer.Tests

# Couchbase currently always skips (see Status table); set Koan_TESTS_COUCHBASE to run against an external cluster
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Couchbase.Tests
```
