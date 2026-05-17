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

- **Per-adapter projects** subclass the base + bind a factory.

## Status

| Adapter | Status | Tests | Notes |
|---|---|---|---|
| **InMemory** | Validated | **21/21 passing** | No container required |
| **Json** | Validated | **21/21 passing** | File-based, temp directory |
| **Mongo** | Validated | **21/21 passing** | Testcontainer `mongo:7`, gracefully skips without Docker. **The user-reported bug scenario, verified end-to-end against a real document database.** |
| **Sqlite** | Blocked | — | `EntitySchemaGuard.EnsureHealthy` reports "healthy" but the underlying table is never created in the `WebApplicationFactory`-hosted setup. Existing `tests/Suites/Data/Connector.Sqlite` works via `TestPipelineFixture`, which suggests the Sqlite adapter's auto-create fallback path needs different hosting plumbing than `WebApplicationFactory<Program>` provides. |
| **Postgres** | Blocked | — | Container startup fails with `InvalidOperationException: cannot hijack chunked or content length stream` — a bug in `DotNet.Testcontainers 1.7.0-beta.2269` that fires when starting containers with `--env` flags. Workaround pattern in `tests/Shared/Koan.Testing/Fixtures/MongoContainerFixture.cs` (Docker CLI fallback) could be ported, or upgrade the Testcontainers stack. |
| **Redis** | Blocked | — | Same Testcontainers hijack-stream bug, even though Redis needs no env vars. |
| **SqlServer** | Not started | — | Would hit the same Testcontainers bug as Postgres until the stack is upgraded. |
| **Couchbase** | Not started | — | Same constraint. |

**Coverage today: 63 tests passing across 3 adapters covering the full EntityController surface.** The DATA-0092 contract is validated against an in-memory store, a file-based store, and a real Mongo container — proving:
- The orchestrator's structured sort contract works
- The in-memory sort fallback for adapters that can't push deep-path down works
- The original-bug scenario (`?sort=-Sightings.LastChangedAt` with pagination on Mongo) is correctly fixed

## Two bugs caught and fixed during this matrix build

1. **Mongo's `Skip(0).Limit(int.MaxValue)`** (shared `QueryExtensions.ApplyPaging`) silently dropped result sets. Fixed by skipping pagination application entirely when no pagination is requested.
2. **`Data<T,K>.QueryWithCount` pagination-flag semantics** — when the orchestrator paginates in memory after refetching unpaginated (the sort-not-pushed-down fallback path), it now sets `RepositoryHandledPagination = true`. The flag's downstream contract with `EntityEndpointService` is "Items is already a page; don't paginate again." Previously the orchestrator only set this flag true when the *repository* handled pagination, causing the web layer to paginate-again the already-paginated window — yielding empty results for `page > 1` on every adapter that couldn't push sort down.

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
```
