# EntityController Surface — Per-Adapter Matrix

End-to-end HTTP tests that exercise the full `EntityController<T>` surface against each Koan data adapter. Validates the DATA-0092 / DATA-0093 sort contract holds across backends, including the original-bug scenario (`?sort=-Sightings.LastChangedAt` with pagination on a complex entity).

## Architecture

- **`Koan.Web.AdapterSurface.TestKit`** — shared library. Defines:
  - `Widget` + `Sighting` (entity with scalar + nested collection)
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

- **Per-adapter projects** (one per backend):
  - `Program.cs` — minimal Koan host with that adapter's config
  - `{Adapter}AdapterFactory.cs` — implements `IAdapterTestFactory`
  - `{Adapter}AdapterSurfaceSpecs.cs` — concrete class binding the base specs

## Status

| Adapter | Status | Notes |
|---|---|---|
| InMemory | **21/21 passing** | No container required |
| Json | **21/21 passing** | File-based, temp directory |
| Mongo | **21/21 passing** | Testcontainer `mongo:7`, gracefully skips without Docker. **The user-reported bug scenario.** |
| Sqlite | Scaffolded — schema bootstrap needs investigation | Test host fails schema auto-create; existing `tests/Suites/Data/Connector.Sqlite` uses `TestPipelineFixture` infrastructure that may need porting |
| Postgres | Scaffolded — apply same pattern as Mongo | `PostgresContainerHelper` provided in TestKit |
| Redis | Scaffolded — apply same pattern as Mongo | `RedisContainerHelper` provided in TestKit |
| SqlServer | Not started — same pattern applies | Use Testcontainers + connection string injection |
| Couchbase | Not started — same pattern applies | Use Testcontainers + N1QL bootstrap |

## Adding a new adapter

1. Copy a working adapter project as a template (`Json` for file-based, `Mongo` for container-based).
2. Rename namespace, project, csproj refs.
3. Update `Program.cs` to set `Koan:Data:Sources:Default:Adapter` to the new adapter name.
4. Update the `Factory` class:
   - Container-based: instantiate the matching `*ContainerHelper`, inject `ConnectionString` via `ConfigureAppConfiguration`.
   - File-based: pick a temp path; clean up in `DisposeAsync`.
   - Implement `ResetAsync()` — drop the database / clear the store.
5. Implement `WebApplicationFactoryContentRoot` assembly attribute (workaround for `tests/Directory.Build.props` redirecting bin to `%TEMP%`).
6. Spec class is one-liner: `public sealed class XAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<XAdapterFactory> { ... }`.

## Running

```bash
# All adapters that don't need Docker
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.InMemory.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Json.Tests

# Adapters that need Docker; tests gracefully Skip without it
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Mongo.Tests
```

## Why this exists

This matrix proves DATA-0092 / DATA-0093 end-to-end. The original bug — `?sort=-Sightings.LastChangedAt` silently dropped — is verified fixed against InMemory, Json, and Mongo via real HTTP requests through `EntityController<Widget>`. The Mongo run uses a real Mongo container, validating the orchestrator's in-memory sort fallback works correctly when adapters can't push deep-path sort down natively.

Two bugs were caught and fixed during this matrix build:
1. Mongo's `Skip(0).Limit(int.MaxValue)` (shared `ApplyPaging`) silently dropped results.
2. `Data<T,K>.QueryWithCount`'s `RepositoryHandledPagination` flag had load-bearing semantics that the web layer relied on; the orchestrator's in-memory fallback was setting it to `false` even after paginating in memory, causing `EntityEndpointService` to paginate a second time and return empty pages.
