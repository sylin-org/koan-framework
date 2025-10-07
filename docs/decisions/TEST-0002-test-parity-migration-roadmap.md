# TEST-0002 Test parity migration roadmap

## Contract
- **Inputs:** Legacy `tests.old` portfolio, migrated `tests/` suites, QA feedback prioritizing pillar coverage (Core → Data → Web → AI → Jobs → Storage → Media → Cache → Canon).
- **Outputs:** Authoritative migration plan that restores parity on the new Koan testing platform and a living task list that teams update as suites land.
- **Error modes:** Stalled migrations due to missing fixtures or infrastructure, duplicated effort between domains, or shipping pillars without regression coverage.
- **Success criteria:** Every pillar has greenfield suites that cover legacy behaviors, migration tasks are tracked here with status updates, and phase two work queues pick up remaining modules without blocking pillar readiness.

## Edge cases to watch
1. Pillar teams may depend on fixtures that still live in `tests.old`; block the deletion of those assets until all dependent tasks flip complete.
2. Some connectors require Docker or external services—ensure TestPipeline fixtures or seed packs are ready before attempting porting, otherwise mark the task as blocked with owner and dependency.
3. Sample apps that double as regression suites (e.g., S4 GraphQL) should be migrated only after their underlying pillar APIs are stable; note any cross-pillar coupling in the task list to avoid deadlocks.
4. Redis / Postgres containers can exhaust local ports on Windows runners; integration suites must honour existing Docker probe helpers to keep CI green.
5. When parity reveals missing product features (not just tests), open architecture issues instead of expanding this ADR beyond coverage scope.

## Context
The greenfield Koan testing platform (TEST-0001) replaced the legacy tree, but large portions of the old suites—especially for core pillars—remain unmigrated. QA analysis identified critical gaps across pillars that block confidence in upcoming releases.

## Decision
Adopt a pillar-first migration program that completes Core, Data, Web, AI, Jobs, Storage, Media, Cache, and Canon coverage before addressing the remaining legacy suites. All work items live in the updateable checklist below; pillar owners incrementally mark tasks as complete or blocked and reference associated PRs. Phase two captures everything else so we sustain momentum without losing track of long-tail suites.

## Task list
Update this section as work ships. Use `[x]` when done, `[ ]` when pending, and annotate blockers inline (e.g., `[#123 blocked: waiting on fixture]`).

### Core
- [x] Validate options extension edge cases under `Suites/Core/Unit` (covers `OptionsExtensionsTests`).
- [x] Add `JsonUtilities` error-path regression specs mirroring legacy coverage.

### Data
- [x] Migrate provider-agnostic specs (lifecycle events, partition routing, cross-provider moves, vector resolution) into `Suites/Data/Core` (`Koan.Tests.Data.Core`).
- [x] Recreate backup/export flow tests with deterministic seed packs (`EntityTransferDsl` spec, Koan.Tests.Data.Core).
- [ ] Stand up connector suites for InMemory, Json, Mongo, Postgres, Redis, Sqlite, Backup, and Weaviate (capabilities, CRUD, bulk/health).
	- [x] InMemory (`tests/Suites/Data/Connector.InMemory/Koan.Data.Connector.InMemory.Tests`) – CRUD, partition isolation, batch, instructions, and capability probes.

### Web
- [ ] Port Canon admin/entities/models controller specs using TestPipeline HTTP harnesses.
- [ ] Migrate web auth + roles regression tests (policies, capability checks, discovery).
- [ ] Reintroduce web backup endpoint coverage.

### AI
- [ ] Populate `Suites/AI/Core` with streaming, adapter fallback, and failure-mode specs.
- [ ] Extend routing specs to cover unhealthy sources and missing metadata scenarios.

### Jobs
- [ ] Create `Suites/Jobs/Unit` with scheduler, cron parsing, and worker lifecycle tests.
- [ ] Add integration specs for retry/backoff semantics and orchestrated job execution.

### Storage
- [ ] Port storage lifecycle, metadata propagation, and error handling tests into `Suites/Storage`.
- [ ] Validate cross-provider behavior using existing seed packs once fixtures are ready.

### Media
- [ ] Recreate media transcoding, metadata extraction, and fallback tests on the new platform.

### Cache
- [x] Add specs for `CacheSingleflightRegistry`, `CacheValue`, and `MemoryCacheStore` (live under `Koan.Tests.Cache.Unit/Specs`).
- [x] Restore Redis adapter integration coverage using shared Docker fixtures (`Koan.Cache.Adapter.Redis.Tests`).

### Canon
- [ ] Port remaining domain tests (observer notifications, AppHost shortcuts, requested view reprojection, skip distribution, option validation) into `Suites/Canon/Unit`.
- [ ] Add controller/API specs to replace legacy web coverage once HTTP fixtures land.

### Phase two backlog
Track long-tail suites here; move items to the pillar list if priorities change.
- [ ] Orchestration (CLI, Docker/Podman connectors, Compose renderer, E2E scenarios).
- [ ] Secrets (core and Vault connector).
- [ ] MCP, Recipes, DocMind sample suites.
- [ ] Sample app integrations beyond S2 (S0, S1, S4, S6, S13).
- [ ] Any additional helpers or diagnostics worth preserving from `tests.old` after pillar parity.
