# Claude Instructions for Koan Framework

## Core Behavioral Guidelines

- **Avoid sycophancy**. Be direct, helpful, and constructive.
- **Challenge ideas** when you see better approaches. Provide pros/cons analysis.
- **Act as senior technical advisor** to an experienced architect.
- **Respect decisions**. Once architectural decisions are made, they become framework canon.

## Koan Framework Expertise

Framework knowledge is provided through **Agent Skills** in `.claude/skills/` that load on-demand based on conversation patterns.

### Pattern Recognition → Skill Invocation

| When You See | Invoke Skill |
|--------------|--------------|
| Entity work, CRUD, data access, repositories | `koan-entity-first` |
| Project setup, Program.cs, initialization | `koan-bootstrap` |
| Multiple providers, switching databases | `koan-multi-provider` |
| Errors, boot failures, debugging | `koan-debugging` |
| REST APIs, controllers, authentication | `koan-api-building` |
| AI features, embeddings, vector search | `koan-ai-integration` |
| Data modeling, relationships, validation | `koan-data-modeling` |
| New projects, learning Koan basics | `koan-quickstart` |
| Complex relationships, N+1 queries | `koan-relationships` |
| Performance issues, large datasets | `koan-performance` |
| Vector database migration | `koan-vector-migration` |
| `[Cacheable]`, L1/L2, cache coherence, TTL, `EntityContext.NoCache()` | `koan-caching` |
| MCP server development | `koan-mcp-integration` |

**Full Skills Catalog**: `.claude/skills/README.md` (descriptions, learning paths, examples)

## Koan Framework Core Principles

- **"Reference = Intent"**: Adding package references automatically enables functionality via `KoanAutoRegistrar`
- **Entity-First Development**: `Todo.Get(id)`, `todo.Save()` patterns with automatic GUID v7 generation
- **Multi-Provider Transparency**: Same entity code works across SQL, NoSQL, Vector, JSON stores
- **Self-Reporting Infrastructure**: Services describe their capabilities in structured boot reports
- **Integration Tests as Canon (ARCH-0079)**: Every adapter, connector, coherence channel, and pillar core ships at least one integration spec that goes through real `AddKoan()` reflective discovery using `KoanIntegrationHost`. Unit tests with fakes are insufficient — they structurally cannot reveal composition or shared-resource bugs. See [tests/README.md](tests/README.md#integration-tests-are-canon-arch-0079) for the canon pattern.

### Critical Anti-Patterns to Detect

**Immediate red flags** that trigger `koan-entity-first` skill with anti-patterns:
- Manual `IRepository<T>` interfaces
- Injecting repositories into services
- Manual service registration in Program.cs (except via `KoanModule` / `KoanAutoRegistrar`)
- Custom ORM/DbContext usage instead of Entity<T>
- Provider-specific code without capability detection

## Framework Utilities

**Before writing new helper methods**, check if Koan Framework already provides them:

### Bootstrap & Modules (ARCH-0086)
- **KoanModule** (`Koan.Core`) — Boot-time module primitive. Extend it to author one self-describing unit: `Id`, `Register(services)` (DI), `Start(sp, ct)` (ordered one-time startup), `Report(...)` (provenance). It implements `IKoanAutoRegistrar`, so existing source-gen discovery + `[Before]`/`[After]` ordering apply unchanged. Preferred over hand-writing `IKoanInitializer` / `IKoanAutoRegistrar`.
- **[KoanDiscoverable]** + **KoanRegistry.GetDiscoveredImplementors** (`Koan.Core`) — Mark an *interface* `[KoanDiscoverable]` and its implementers are auto-registered into `KoanRegistry` (build-time generator + runtime `RegistryManifestLoader`), queried with `KoanRegistry.GetDiscoveredImplementors(typeof(T))`. Replaces bespoke `AppDomain.GetAssemblies()` scans (which miss lazily-loaded assemblies). Used by the auth contributor / flow-handler pipelines.
- **PartitionNameValidator** (`Koan.Data.Core`) — Enforced at `EntityContext.With(partition:)`: a partition name must be a GUID or contain only letters/digits/`-`/`.`/`_`, else `ArgumentException`. Prevents distinct partitions colliding after identifier sanitization (DATA-0077 §4).

### Orchestration & Discovery
- **ConnectionStringParser** (`Koan.Core.Orchestration`) - Parse/build connection strings for Postgres, SQL Server, MongoDB, Redis, SQLite
- **ServiceDiscoveryAdapterBase** (`Koan.Core.Orchestration`) - Base class for discovery adapters with container/local/Aspire detection

### Configuration & Options
- **OptionsExtensions** (`Koan.Core.Modules`) - `AddKoanOptions<T>()`, `AddKoanOptionsWithValidation<T>()` for consistent options registration

### Web API Utilities
- **EntityQueryParser** (`Koan.Web.Queries`) - Parse filter, sort, pagination, field selection from query strings
- **PatchNormalizer** (`Koan.Web.PatchOps`) - Normalize and validate JSON Patch operations
- **EntityController<T>** (`Koan.Web.Controllers`) - Full REST API base controller with CRUD + Query + Patch

### Cache (ARCH-0075 + ARCH-0078 · v0.7.0)
- **[Cacheable]** (`Koan.Cache.Abstractions.Policies`) — Entity-friendly attribute. `[Cacheable(300)]` opts an entity into transparent L1/L2 caching with sane defaults. Power users drop to `[CachePolicy]` for custom key templates / method-scoped policies
- **Fresh-or-null read contract (ARCH-0078)** — Default reads return `null` past `AbsoluteTtl`. **SWR is opt-in only** via `[Cacheable(AllowStaleForSeconds = N)]` or `.AllowStaleFor(TimeSpan)` on the fluent builder. No global adapter toggles — the per-call/per-policy opt-in is the only switch.
- **LayeredCache** (`Koan.Cache.Topology`) — Composition-based L1/L2 orchestrator. Verbs: `Read`/`Write`/`Evict`/`Touch`/`EnumerateByTag`. `ApplyRemoteInvalidation` is L1-only (architectural invariant)
- **CoherenceCoordinator** (`Koan.Cache.Coherence`) — `IHostedService` routing `ICacheCoherenceChannel` messages → `LayeredCache.ApplyRemoteInvalidation`. Origin filter prevents echo. Honors `CoherenceMode.AutoDetect`/`Required`/`Disabled`
- **CacheKey.For<T>(id, partition)** (`Koan.Cache.Abstractions.Primitives`) — Canonical entity key builder; eliminates stringly-typed concatenation
- **EntityCacheExtensions** (`Koan.Cache`) — `entity.Uncache()`, `EntityCache<T,K>.Flush(id)`, `.FlushAll()` for out-of-band evict from `Koan.Data.Direct` / batch jobs
- **EntityContext.CacheBehavior** (`Koan.Data.Core`) — Per-request opt-out: `NoCache()`, `RefreshCache()`, `WithCacheBehavior(...)`. Writes always invalidate regardless
- **Reference = Intent adapters**:
  - `Koan.Cache.Adapter.Sqlite` — persistent L1 (priority 50, preempts Memory)
  - `Koan.Cache.Adapter.Redis` — L2 + `RedisCoherenceChannel` (priority 100 + 100)
  - `Koan.Cache.Coherence.Messaging` — rides `Koan.Messaging.IMessageBus` (priority 150, preempts Redis pub/sub)
  - `Koan.Cache.Coherence.InMemory` — fallback channel for tests / single-process verification (priority `int.MinValue`)
- **Singleflight** (`Koan.Core.Singleflight`) — Stampede-protection primitive lifted out of cache for cross-pillar reuse

### Background Jobs (JOBS-0005)
- **IKoanJob<TSelf>** (`Koan.Jobs`) — Entity-first jobs: `MyJob : Entity<MyJob>, IKoanJob<MyJob>` with `static Task Execute(MyJob, JobContext, CancellationToken)`. Auto-discovered (`[KoanDiscoverable]`) — no queues/workers/repositories to wire
- **`.Job` / `.Jobs` accessors** (`Koan.Jobs`) — `myJob.Job.Submit(action)`, `MyJob.Jobs.Trigger(action)` / `.Cancel(id)` / `.Where(...)`, batch `list.Submit(action)` (C# 14 extension members — instance `Job`, static `Jobs`; no source generator)
- **Job attributes** (`Koan.Jobs`) — `[JobAction(action, Timeout/MaxAttempts/OnFailure/Lane/MaxConcurrency/Schedule/Deadline/MaxReschedules)]`, `[JobChain(a,b,c)]` (linear auto-advance), `[JobIdempotent(keys)]` (coalesce duplicates), `[JobGate(member)]` (shared resource gate — `member` is a property *or* an async resolver method `Task<string?>(IServiceProvider, CancellationToken)` for runtime-derived keys, §18), `[JobPersistence(Auto|InMemory|DataStore)]` (per-type durability routing), `[ParallelSafe]` (opt out of per-entity serialization)
- **JobContext** (`Koan.Jobs`) — handler control verbs: `ctx.Reschedule(after|until)` (defer without consuming a retry), `ctx.Backoff(after, key)` (set a cross-node resource gate), `ctx.ContinueWith(action)` / `ctx.StopChain`, `ctx.Progress(fraction, msg)`; read-only `ctx.State` for stateful backoff decisions
- **Work-item write safety (ADR §17)** — **mutate the entity passed to `Execute`** (don't reload-and-save your own copy): the orchestrator auto-saves that reference only if it changed (`save-if-changed`), so an untouched reference is never written and a handler's own write is never clobbered. And a work-item id is its **ordering key** — jobs for the same `(WorkType, WorkId)` run **one at a time by default** (FIFO-group; `[ParallelSafe]` opts out). Different entities still parallelize fully
- **Capability ladder** — in-memory → durable (`DataJobLedger` over `Entity<JobRecord>`; follows any data adapter) → distributed (competing consumers on the shared ledger) → `+bus` (`Koan.Jobs.Transport.Messaging`, cross-node push-dispatch). **Constant at-least-once + idempotent contract across all tiers**; the ledger is the single source of truth (claim = atomic CAS, never a move). Scheduling is initiator-driven (`Schedule` re-submits on a cadence: interval / cron via Cronos / `@boot` / `@continuous`); transactional outbox + retention are automatic on the durable tier (Completed/Cancelled past `ArchiveAfter` 7d, Failed/Dead past `FailedAfter` 30d, optional `RetainPerWorkType` cap) and ledger reads are indexed + pushed down. For bulk, window the source — a cursor-conveyor (`ctx.ContinueWith` self-loop) drains it through a handful of rows, not one per item; don't mint a job per row (JOBS-0005 §19)
- **Scale tiers (JOBS-0005 §20)** — **`JobMetric`** + `JobsOptions.MetricsEnabled` (opt-in, default off): a worker-batched, node-sharded throughput rollup that **survives retention** — active counts come from the indexed ledger, this is the completed/failed *history*; read with `JobMetric.Summary(workType, from, to)`. **Contention-free claim**: on durable adapters the default `Optimistic` strategy now marks `Running` via the reusable **`IConditionalWriteRepository.ConditionalReplaceAsync(model, guard)`** (`DataCaps.Write.ConditionalReplace`, `Koan.Data.Abstractions`) — a compare-and-set / optimistic-concurrency primitive (SQLite/PG/SqlServer/Mongo, forwarded by `RepositoryFacade`) — so each ready job runs on exactly one node (no duplicate executions). Pending: TTL-index retention (§20.4)
- **Authoring guide**: [Background Jobs How-To](docs/guides/jobs-howto.md) · **ADR**: docs/decisions/JOBS-0005

### Entity Lifecycle
- **[Timestamp]** (`Koan.Data.Abstractions`) — `[Timestamp]` set-once on creation, `[Timestamp(OnSave = true)]` set on every save
- **Entity Transfer** (`Koan.Data.Core`) — `Entity<T>.Copy()`, `.Move()`, `.Mirror()` fluent builders for cross-context transfers

### AI — Entity-Aware Operations
- **EntityAi** (`Koan.Data.AI`) — `EntityAi.Embed(entity)`, `.Chat(msg, entity)`, `.Ocr(entity)` with convention inference
- **[MediaAnalysis]** (`Koan.Data.AI`) — `[MediaAnalysis(Analysis = Describe | Ocr)]` auto-processes media on upload
- **MediaAnalysisEmbeddingBridge** (`Koan.Data.AI`) — Cross-modal search: analysis results feed into `[Embedding]` text

### Common Patterns
- **Guard Clauses** (`Koan.Core.Utilities.Guard`) - `Must.NotBeNull()`, `Be.Positive()`, `NotBe.Default()`
- **Entity Static Methods** - `Todo.Get(id)`, `Todo.Query(x => ...)`, `todo.Save()`, `todo.Delete()`

### Decision Framework: Static vs DI

**Use static utility when**:
- Pure function (no side effects)
- Zero allocation on hot paths
- Used across many assemblies
- Testable through inputs only
- Examples: parsing, validation, transformation

**Use DI service when**:
- Needs configuration at runtime
- Has mutable state
- Requires lifecycle management
- Needs mocking in tests
- Examples: repositories, HTTP clients, caches

**Full Catalog**: [Framework Utilities Guide](docs/guides/framework-utilities.md)
**Strategic READMEs**: Check directory READMEs for contextual utilities

## Skill Evolution Pattern

**Proactively suggest new skill creation** when you detect:

### Skill Creation Triggers
- **New pillar/domain**: Significant new framework capability area (e.g., Security, Observability)
- **Pattern consolidation**: 3+ related patterns or ADRs in same domain
- **Documentation threshold**: Guide/documentation >150 lines for single topic
- **Repeated troubleshooting**: Same domain issues appearing multiple times
- **Complex integration**: New connector category or cross-pillar pattern

### Skill Proposal Format
```markdown
## New Skill Opportunity: [domain-name]

**Trigger**: [What prompted this - new pillar, pattern consolidation, etc.]

**Justification**:
- Domain scope: [Brief description]
- Content volume: [Estimated lines, existing docs to migrate]
- Reusability: [How many scenarios benefit]
- Complexity: [Learning curve that justifies progressive disclosure]

**Proposed Structure**:
- `SKILL.md`: [Core patterns to include]
- `examples/`: [If applicable]
- `templates/`: [If applicable]
- Tier: [1-4, with rationale]

**Pattern Recognition Entry**: [Suggested trigger phrase for skills table]
```

### Skill Update Triggers
Also suggest **updates to existing skills** when:
- New ADR affects skill domain (e.g., DATA-00XX for data-modeling skill)
- Anti-pattern emerges that should be documented
- Performance benchmark data changes materially
- New framework capability affects skill patterns

## Container Development

Never use `docker compose up` or `build` if `start.bat` script is available.

---

**Framework Version**: v0.6.3
**Skills Location**: `.claude/skills/`
