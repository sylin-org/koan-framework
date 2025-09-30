# DATA-0075: Entity-level schema guard and adapter provisioning reset

**Contract**

- **Inputs:** `Entity<TEntity,TKey>` static calls, `Data<TEntity,TKey>` facade operations, orchestrator capabilities, adapter health telemetry.
- **Outputs:** Deterministic one-time schema ensure per entity per dataset, shared health cache with invalidation hooks, structured events for ensure/repair outcomes.
- **Error modes:** Orchestrator unreachable, schema mismatch, provider-specific missing-table/collection faults; guard escalates via `SchemaUnhealthyException` after single retry.
- **Success criteria:** No redundant per-call `EnsureOrchestrated` invocations, adapters emit zero spam logs on healthy calls, first-call ensures observable via metrics, guard re-arms automatically after schema faults, SQLite and Mongo implementations completed before extending to other adapters.

Status: Proposed

## Context

Current relational adapters (`SqliteRepository`, `SqlServerRepository`, `PostgresRepository`) invoke schema validation/provisioning every time they open a connection. Even with caching and singleflight dedupe, the hot path still performs cache lookups, emits debug logs (notably in SQLite), and owns complex retry logic for missing tables. Non-relational adapters such as Mongo only implement readiness checks and run bespoke index creation inside the repository. This scattered behavior complicates consistency, makes observability patchy, and keeps schema governance buried deep in provider code.

Koan’s entity-first story should guarantee that schema provisioning happens once per entity per storage set, at the API boundary, with a clear recovery flow when a downstream store loses its tables/collections. Because we are still in greenfield mode, we prefer a break-and-rebuild of the provisioning stack instead of incremental patches to legacy hooks.

## Decision

1. **Move orchestration to an entity-level guard.** Introduce an `EntitySchemaGuard<TEntity,TKey>` that is invoked by every `Entity<TEntity,TKey>` and `Data<TEntity,TKey>` static API before touching a repository. The guard runs once per process (per entity + dataset) and records health in a shared cache.
2. **Expose a provider capability.** Repositories that can participate supply an `ISchemaHealthContributor<TEntity,TKey>` interface with `EnsureHealthyAsync(ct)` and `InvalidateHealth()` members. Adapters opt-in via DI registration; the guard skips providers that do not implement the capability.
3. **Standardize error signaling.** When adapters hit provider-specific ‘missing table/collection’ faults, they translate them into a new `SchemaUnhealthyException`, allowing the guard to invalidate state and retry exactly once.
4. **Surface observability.** Every ensure attempt publishes structured events/metrics (`EntitySchemaEnsured`, `EntitySchemaRepairFailed`), including latency and provider identity.
5. **Retire adapter-local ensure logic.** All redundant `_healthyCache`, log spam, and inline ensure calls inside repositories are deleted once the guard is in place. Providers retain only capability hooks and fallbacks that are strictly necessary (e.g., SQLite’s local create when orchestrator disallows DDL).
6. **Execution order:**
   - Phase A: Implement guard capability plumbing, migrate SQLite to the new contract, delete its legacy ensure paths.
   - Phase B: Implement guard capability in Mongo (collection readiness + index provisioning), removing bespoke lazy initialization to rely on the shared guard.
   - Phase C: Migrate remaining adapters (SqlServer, Postgres, Redis, JSON, etc.) and delete their leftover ensure routines.

## Implementation sketch

- Add guard service and capability definitions to `Koan.Data.Core`. Guard uses `AsyncLazy` plus `Koan.Core.Infrastructure.Singleflight` to dedupe ensures.
- Extend `ServiceCollectionExtensions` so repositories register their `ISchemaHealthContributor<TEntity,TKey>` alongside `IDataRepository<TEntity,TKey>` implementations.
- Teach `Entity<TEntity,TKey>` and `Data<TEntity,TKey>` helpers to await the guard (respecting cancellation tokens) before invoking repository methods.
- Provide `EntitySchemaGuard.WarmAsync(IEnumerable<Type> entities, CancellationToken)` so applications can pre-provision during startup if desired.
- Rebuild SQLite repository: remove `EnsureOrchestrated` calls from connection open, keep only projection polling and fallback DDL inside `EnsureHealthyAsync` implementation.
- For Mongo, move `GetCollectionAsync`’s collection/index creation into `EnsureHealthyAsync`; the repository becomes a thin wrapper that assumes the guard prepared the collection.
- Update other adapters in Phase C, deleting legacy caches and making sure they throw `SchemaUnhealthyException` when the guard must re-arm.

## Migration & break plan

- This is a break-and-rebuild exercise: legacy ensure code is removed rather than shimming around it. Consumers will receive the guard automatically through the entity facade; there are no user-facing API changes besides new diagnostics.
- During the transition, temporary feature branches staged per phase keep the repo coherent—no partial adapters merged without the guard wiring.
- Samples and tests that previously depended on repository-level ensures are updated to rely on the guard or explicit warm-up calls.

## Risks & mitigations

- **Async deadlocks:** Ensure guard uses fully async continuations and never blocks on `Result`. Integration tests will cover synchronous callers.
- **Thundering herd on crash-loop stores:** Guard retries only once per failure; further calls propagate the exception so operators can intervene.
- **Non-relational parity:** Mongo’s readiness gating must align with the new guard; capability opt-in allows incremental adoption while still removing legacy logic during this effort.

## Follow-up actions

1. Implement guard primitives and SQLite migration (Phase A) → blocks current log spam issues.
2. Migrate Mongo readiness into the guard pattern (Phase B).
3. Sweep remaining adapters, excising legacy `_healthyCache` / `EnsureOrchestrated` code (Phase C).
4. Add docs and diagnostic dashboards for ensure events.
5. Write regression tests that drop tables/collections mid-run to validate guard invalidation behavior.
