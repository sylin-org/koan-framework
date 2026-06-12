# EntityController Surface — Per-Adapter Matrix

End-to-end HTTP tests that exercise the full `EntityController<T>` surface against each Koan data adapter, plus partition routing (`?set=` / `EntityContext.With(partition: ...)`) and cross-partition transfer (`Entity<T>.Copy()` / `Move()` / `Mirror()`, `Data<T,K>.CopyPartition` / `MovePartition` / `MoveFrom().To()`).

## Architecture

- **`Koan.Web.AdapterSurface.TestKit`** — shared library. Defines:
  - `Widget` + `Sighting` (entity with scalar + nested collection, `[StorageName("widgets_surface")]`)
  - `WidgetController` (`EntityController<Widget>` on `/api/widgets`)
  - `IAdapterTestFactory` (per-adapter contract: client, services, reset, availability)
  - `IAdapterCapabilities` (per-adapter capability flags — adapters declare what they support; specs that need a missing capability skip cleanly with a clear reason)
  - **Three spec base classes**, all `IClassFixture<TFactory>`:
    - `AdapterSurfaceSpecsBase<TFactory>` — 31 HTTP surface specs (CRUD, sort, pagination, PATCH ×3 content types, DELETE bulk / by-query / all, body-query, filter)
    - `AdapterPartitionSpecsBase<TFactory>` — 8 partition routing specs (?set= isolation on read/write/delete/patch/bulk)
    - `AdapterTransferSpecsBase<TFactory>` — 9 cross-partition transfer specs (`Copy`, `Move`, fluent `MoveFrom().To()`, `CopyPartition`, `MovePartition`, `ClearPartition`, predicate-filtered transfer)
  - `Containers/` — Testcontainers helpers for Mongo, Postgres, Redis, SqlServer, Couchbase (all on `Testcontainers 4.11.0` per-image packages)

- **Per-adapter projects** subclass each of the three base classes (one-liner each) and declare capabilities on the factory.

## Capability gating

Each adapter declares what it supports via `IAdapterCapabilities` on its factory. Specs that need a missing capability skip with `[FactoryName] does not support <capability>`. The default for `SupportsDeleteByQuery` and `SupportsQueryStringFilter` is **false** because the framework's string-query path requires `IStringQueryRepository` (only the relational adapters implement it) and silently degrades to "operate on all" otherwise — a real adapter inconsistency the matrix surfaced.

## Status

48 specs per adapter (31 surface + 8 partition + 9 transfer). Numbers below are passing / skipped / failing.

| Adapter | Specs (p/s/f) | Notes |
|---|---|---|
| **InMemory** | **42 / 6 / 0** | No container required. Partition routing + cross-partition transfer fully validated here. |
| **Json** | **42 / 6 / 0** | File-based, temp directory. Partition + transfer fully validated. |
| **Sqlite** | **25 / 23 / 0** | File-based. Partition + transfer opted out: `ensureCreated` under `EntityContext.With(partition: X)` doesn't produce the partition-suffixed table at the framework level. |
| **Mongo** | **25 / 23 / 0** | Testcontainer `mongo:7`. Surface specs all green; partition/transfer opted out pending the framework partition-routing fix. The original `?sort=-Sightings.LastChangedAt` bug stays validated end-to-end here. |
| **Postgres** | **25 / 23 / 0** | Testcontainer `postgres:16-alpine`. Same partition/transfer opt-out as Sqlite. |
| **Redis** | **25 / 23 / 0** | Testcontainer Redis. Most partition ops work but bulk-upsert and collection-read with `?set=` leak across to default; opted out to keep the matrix honest. |
| **SqlServer** | **25 / 23 / 0** | Testcontainer MsSql. Same partition/transfer opt-out as Sqlite/Postgres. |
| **Couchbase** | **0 / 48 / 0** | Whole suite cleanly skipped: `Testcontainers.Couchbase` exposes KV and management on separate random host ports, but Koan's `CouchbaseClusterProvider` derives the management URL from the single KV connection string. Set `Koan_TESTS_COUCHBASE=...` to run against an externally provisioned cluster. |

**Total: 209 active passing, 159 cleanly skipped, 0 failing across 8 adapters.**

The 6-skip baseline (3 PATCH variants + 2 DeleteByQuery + 1 ?filter=) reflects the framework limitations documented below; the 23-skip pattern on relational + Mongo + Redis reflects partition-routing opt-out.

## Skipped HTTP surface ops (framework follow-ups)

These six specs skip even on the most capable adapters:

| Spec | Reason |
|---|---|
| `PatchJsonPatch_replace_updates_target_field` | ASP.NET Core's media-type matcher treats `application/json-patch+json` as a JSON variant matching `[Consumes("application/json")]` (the PatchPartial action), producing an `AmbiguousMatchException`. Until the framework adds an explicit ConsumesMatcherPolicy override or removes the application/json handler, the JsonPatch route is unreachable via standard routing. |
| `PatchJsonPatch_against_missing_id_returns_404` | Same routing ambiguity. |
| `PatchMergePatch_partial_object_merges_into_entity` | Same routing ambiguity. The Merge Patch RFC content type `application/merge-patch+json` is similarly shadowed by `application/json`. |
| `GetCollection_querystring_filter_returns_matching_subset` | Requires server-side string-query support. Most adapters only implement `ILinqQueryRepository`, not `IStringQueryRepository`, and silently degrade to "return all" when given a JSON filter string. Relational adapters claim support but the JsonFilterBuilder → adapter handoff also has gaps (see below). |
| `DeleteByQuery_removes_matching_entities` | Same as above — DELETE `?q=` routes through `IStringQueryRepository`; degrade-to-all is unsafe. |
| `DeleteByQuery_without_q_returns_400` | Gated by the same capability flag. |

## Skipped partition / transfer ops (framework follow-up)

All 17 partition + transfer specs skip on Sqlite, Postgres, SqlServer, Redis, and Mongo. The root cause is a single framework issue: writes routed through `EntityContext.With(partition: X)` arrive at the adapter without an auto-created partition-suffixed table/collection/keyspace. `ensureCreated` issued under the partition context also doesn't produce one — likely a `StorageNameRegistry` cache or schema-health-cache interaction. The `In-Memory` and `Json` adapters work because they store per-partition by key prefix in-process and have no schema layer.

## Bugs caught and fixed during the surface-matrix expansion

This expansion was a productive net negative — it found more framework issues than it fixed because the matrix exercises real edges. The fixes that did land in the framework:

1. **Mongo's `Skip(0).Limit(int.MaxValue)`** (shared `QueryExtensions.ApplyPaging`) silently dropped result sets. Fixed by skipping pagination application entirely when no pagination is requested.
2. **`Data<T,K>.QueryWithCount` pagination-flag semantics** — when the orchestrator paginates in memory after refetching unpaginated (the sort-not-pushed-down fallback path), it now sets `RepositoryHandledPagination = true`. The flag's downstream contract with `EntityEndpointService` is "Items is already a page; don't paginate again." Previously the orchestrator only set this flag true when the *repository* handled pagination, causing the web layer to paginate-again the already-paginated window — yielding empty results for `page > 1` on every adapter that couldn't push sort down.
3. **Postgres + SqlServer `KoanAutoRegistrar` missing `services.AddRelationalOrchestration()`** — Sqlite's auto-registrar had it but the other two relational adapters didn't, so `IRelationalSchemaOrchestrator` was unresolvable when the connector tried to provision tables under `WebApplicationFactory`-style hosting. Fixed both.
4. **DDL executors had fire-and-forget async** — `MsSqlDdlExecutor`, `PgDdlExecutor`, and `SqliteDdlExecutor` all used `ExecuteScalarAsync()` / `ExecuteNonQueryAsync()` without awaiting. `TableExists` always returned `true` because `Task<object?>` is itself not null, so `CreateTable` never ran and every adapter spec failed with "no such table" / "Invalid object name". Converted all DDL ops to their sync equivalents (DDL is bounded and synchronous by nature). This single fix unblocked Postgres, SqlServer, and Sqlite simultaneously.

## Framework follow-ups raised by the matrix (not yet fixed)

| Area | Issue | Tracked in |
|---|---|---|
| PATCH routing | `[Consumes("application/json")]` shadows `application/json-patch+json` / `application/merge-patch+json` because of the +json structured-suffix rule. The JsonPatch and MergePatch endpoints on `EntityController<T>` are unreachable. | Per-spec skip-with-reason in `AdapterSurfaceSpecsBase` |
| Partition write routing | `ensureCreated` under `EntityContext.With(partition: X)` does not produce the partition-suffixed table on Sqlite/Postgres/SqlServer. Mongo and Redis exhibit related leakage. The `?set=` parameter routes reads correctly but writes silently target the default partition. | Per-adapter `SupportsPartitions = false` in factory |
| `IStringQueryRepository` parity | Most adapters only implement `ILinqQueryRepository`. When a string-query operation arrives at one of them (`DELETE /?q=`, `GET /?filter=`), the operation silently degrades to "operate on all" instead of returning 501 or 400. | Per-adapter `SupportsDeleteByQuery = false` / `SupportsQueryStringFilter = false` defaults |
| Couchbase Testcontainers integration | `CouchbaseClusterProvider` derives the management URL by replacing `couchbase://` with `http://` on the single KV connection string. Testcontainers exposes KV (11210) and management (8091) on independent random host ports, so cluster init hits the wrong port and the bucket is never created. | Whole Couchbase suite opted out via `UnavailableReason` |

## Adding a new adapter

1. Copy a working adapter project as a template (`Json` for file-based, `Mongo` for container-based).
2. Rename namespace, project, csproj refs.
3. Update `Program.cs` to set `Koan:Data:Sources:Default:Adapter` to the new adapter name.
4. Update the `Factory` class:
   - Container-based: instantiate the matching `*ContainerHelper`, inject `ConnectionString` via `ConfigureAppConfiguration`.
   - File-based: pick a temp path; clean up in `DisposeAsync`.
   - Implement `ResetAsync()` — drop the database / clear the store.
   - Override capability flags on the factory that don't apply (e.g. `public bool SupportsPartitions => false;`).
5. Add `WebApplicationFactoryContentRoot` + `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `Properties/AssemblyInfo.cs` (the parallelization toggle is required because the three spec classes share `AppHost.Current`).
6. Spec classes are one-liners per base:
   ```csharp
   public sealed class XAdapterSurfaceSpecs : AdapterSurfaceSpecsBase<XAdapterFactory> { public XAdapterSurfaceSpecs(XAdapterFactory f) : base(f) { } }
   public sealed class XPartitionSpecs   : AdapterPartitionSpecsBase<XAdapterFactory> { public XPartitionSpecs(XAdapterFactory f) : base(f) { } }
   public sealed class XTransferSpecs    : AdapterTransferSpecsBase<XAdapterFactory>  { public XTransferSpecs(XAdapterFactory f) : base(f) { } }
   ```

## Running

```bash
# Adapters that don't need Docker
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.InMemory.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Json.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Sqlite.Tests

# Adapters that need Docker; tests gracefully Skip without it
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Mongo.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Postgres.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Redis.Tests
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.SqlServer.Tests

# Couchbase currently always skips (see Status); set Koan_TESTS_COUCHBASE to run against an external cluster
dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.Couchbase.Tests
```
