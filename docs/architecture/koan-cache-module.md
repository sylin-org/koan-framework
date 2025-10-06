---
type: ARCHITECTURE
domain: framework
title: "Koan Cache Module Architecture"
audience: [architects, developers, ai-agents]
status: draft
last_updated: 2025-10-06
framework_version: v0.6.3
validation:
  status: not-yet-tested
  scope: docs/architecture/koan-cache-module.md
---

> **Contract**
>
> - **Inputs:** Koan data adapter model, Entity<T> conventions, existing Koan.Core abstractions (KoanEnv, Auto-Registrar, Configuration helpers), and customer scenarios needing low-latency state reuse.
> - **Outputs:** A cohesive module design (`Koan.Cache`) delivering cache client APIs, adapter expansion points, and developer ergonomics aligned with reference=intent.
> - **Failure modes:** Divergent cache APIs across modules, providers that cannot enforce TTL/serialization guarantees, or data adapters that cannot advertise caching capabilities.
> - **Success criteria:** Applications select cache backends declaratively, reuse cached state via terse fluent helpers, and the abstract data layer can consume caching without leaking provider implementation.

## Edge Cases To Plan For

1. Large object payloads (files, vectors) where providers may impose size limits or require streaming.
2. Concurrent writers attempting to mutate the same cache key, especially when providers support atomic operations differently.
3. Eviction or expiration lapses causing stale reads in critical workflows (Canon pipelines, auth tokens).
4. Multi-region deployments where latency-sensitive caches need geo-awareness or read replicas.
5. Provider capability mismatches (e.g., Redis supports pub/sub invalidation, in-memory does not) that must surface predictably to callers.

## Why Koan.Cache

- Koan currently focuses on persistence, messaging, and orchestration. Caching is handled ad-hoc via ASP.NET `IMemoryCache`, Redis clients, or bespoke wrappers—none align with Koan's reference=intent ethos.
- Canon pipelines, task schedulers, and EntityController-heavy APIs frequently need shared state snapshots, throttling counters, or expensive query memoization.
- A dedicated module keeps the developer ergonomics consistent: declarative selection of providers, simple defaults, and automation through Auto-Registrar metadata.

## Module Stack Overview

| Layer                     | Responsibility                                                                         | Key Types                                                                               |
| ------------------------- | -------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `Koan.Cache.Abstractions` | Contracts shared by adapters and consumers.                                            | `ICacheStore`, `CacheItemDescriptor`, `CacheCapabilities`, `ICacheSerializer`           |
| `Koan.Cache`              | Default implementation with DI registrations, static gateway helpers, instrumentation. | `CacheClient`, `Cache`, `CachePipeline`, serializers                                    |
| `Koan.Cache.Adapters.*`   | Provider-specific bridges implemented using Koan data connectors.                      | `Koan.Cache.Adapter.Redis`, `Koan.Cache.Adapter.Memory`, `Koan.Cache.Adapter.Couchbase` |
| `Koan.Cache.Testing`      | Fakes and harnesses for unit testing.                                                  | `InMemoryCacheHarness`, `CacheProbe`                                                    |

The adapters layer reuses Koan data connectors for connection management, metrics, and capability discovery—developers can swap cache providers by adjusting module references and configuration.

## Developer Experience Flow

1. Add the `Koan.Cache` reference (and optionally `Koan.Cache.Adapter.Redis`).
2. Update the module's `KoanAutoRegistrar` to advertise caching intent—no manual service wiring.
3. Configure the cache provider via Koan configuration (environment variables, JSON) aligned with data adapter patterns.
4. Inject `ICacheClient` or use the static `Cache` facade for one-liners.
5. Observe metrics via the standard Koan telemetry pipeline (OpenTelemetry conventions).

### Auto-Registrar Snippet

```csharp
using Koan.Core;
using Koan.Cache;
using Microsoft.Extensions.DependencyInjection;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S8.Content";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCache(); // registers CacheClient, serializers, metrics
        services.AddKoanCacheAdapter("redis"); // resolves Koan.Data.Connector.Redis connection info
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion)
              .AddCapability("cache", cfg["Cache:Provider"] ?? "redis");
    }
}
```

This mirrors the existing AddKoan bootstrap style—no bespoke wiring. The adapter registration inspects known Koan data connectors and loads matching cache providers.

## Cache Facade & Fluent API

To keep call sites terse yet expressive, `Koan.Cache` exposes a static `Cache` helper backed by the scoped `CacheClient`. Each call resolves the provider based on configuration and key scoping rules.

```csharp
using Koan.Cache;
using Koan.Data;

public class TodoProjectionService
{
    public async Task<IReadOnlyList<Todo>> GetOpenTodosAsync(CancellationToken ct)
    {
        return await Cache.WithJson("todo:open:v1")
            .For(TimeSpan.FromMinutes(1))
            .GetOrAddAsync(async innerCt => await Todo.Query("Completed == false", innerCt), ct);
    }
}
```

### Fluent Helpers

| Helper                         | Purpose                                                               |
| ------------------------------ | --------------------------------------------------------------------- |
| `Cache.WithJson(key)`          | JSON-serializes complex objects using configured serializer.          |
| `Cache.WithBinary(key)`        | Stores raw bytes or streams for files.                                |
| `Cache.WithString(key)`        | Optimized path for UTF-8 text content.                                |
| `Cache.WithRecord<TItem>(key)` | Strongly typed upsert/merge using custom serializers.                 |
| `.For(TimeSpan ttl)`           | Sets relative expiration.                                             |
| `.Tag(params string[] tags)`   | Adds cache tags for selective invalidation.                           |
| `.AllowStaleUntil(TimeSpan)`   | Enables "stale-while-revalidate" behaviors when provider supports it. |
| `.PublishInvalidation()`       | Forces adapters supporting pub/sub to emit invalidation messages.     |
| `.Exists(ct)`                  | Lightweight probe that returns `true` when the key is currently cached. |
| `Cache.Exists(key)`            | Global probe without building an entry instance; returns a `ValueTask<bool>`. |

The fluent builder returns a `CacheEntry<T>` that supports `GetAsync`, `GetOrAddAsync`, `SetAsync`, `RemoveAsync`, `TouchAsync`, and the new `Exists` probe. All methods accept `CancellationToken` to stay aligned with Koan data calls.

### Tag and Policy Control Helpers

- `Cache.Tags("todo", "user:123").Flush()` removes distinct keys associated with the provided tags (no `Async` suffix on the fluent method, yet it still returns a `ValueTask<long>`).
- `Cache.Tags("todo").Count()` and `.Any()` surface quick diagnostics for how many entries remain under a tag group.
- `Entity<TEntity, TKey>.Cache` bridges cache policies straight to entity code. `await Todo.Cache.Flush(ct)` gathers tags from `[CachePolicy]` declarations, merges any additional explicit tags supplied by the caller, and issues a tag flush via the cache client. `Count(...)` and `Any(...)` follow the same contract.

Helpers intentionally avoid the `Async` suffix even though they are asynchronous under the hood, matching DX feedback while preserving `ValueTask` signatures for efficient awaiting.

### Static Helper Under the Hood

- The static `Cache` type is a thin proxy that resolves `ICacheClient` via scoped services.
- Consumers preferring DI can inject `ICacheClient` or `ICacheWriter` directly, gaining the same fluent API without static helper.
- Koan CLI tooling can auto-generate strongly typed cache key wrappers based on configuration (future work).

## Core API Implementation Blueprint

| Component                   | Location                          | Description                                                                                              | Dependencies                                      |
| --------------------------- | --------------------------------- | -------------------------------------------------------------------------------------------------------- | ------------------------------------------------- |
| `Koan.Cache.Abstractions`   | `/src/Koan.Cache.Abstractions/`   | Contracts, capability descriptors, policy metadata, marker interfaces.                                   | `Koan.Core` (logging, options)                    |
| `Koan.Cache`                | `/src/Koan.Cache/`                | Implements fluent API, DI bootstrap, singleflight coordinator, serializers, telemetry, policy evaluator. | `Koan.Cache.Abstractions`, `Koan.Core.Telemetry`  |
| `Koan.Cache.Adapter.Memory` | `/src/Koan.Cache.Adapter.Memory/` | `ICacheStore` implementation over `IMemoryCache`.                                                        | `Microsoft.Extensions.Caching.Memory`             |
| `Koan.Cache.Adapter.Redis`  | `/src/Koan.Cache.Adapter.Redis/`  | `ICacheStore` backed by `IDistributedCache` (StackExchange.Redis).                                       | `Microsoft.Extensions.Caching.StackExchangeRedis` |
| `Koan.Cache.Testing`        | `/tests/Koan.Cache.Testing/`      | Harness, fake stores, assertion helpers.                                                                 | xUnit, `Koan.Cache`                               |

### Core Types To Ship

- `ICacheClient`, `ICacheReader`, `ICacheWriter`: thin abstractions swallowing `CacheEntryBuilder<T>`. `ICacheClient` exposes `Store` (for capability introspection) and `CreateEntry(key, contentType)` factory methods.
- `CacheEntryBuilder<T>`: manages options (TTL, tags, stale policy, singleflight). Internally composes `CacheEntryOptions` record and emits to `ICacheStore` via `CacheEnvelope`.
- `CachePolicyAttribute`: see **Policy Attributes & Metadata**.
- `CacheScope`: disposable ambient scope storing prefixes/tenant hints; consumed by builders and interceptors.
- `CacheInstrumentation`: helper emitting metrics/logs, reused by adapters to avoid duplicate code.
- `CacheSingleflightRegistry`: keyed by `CacheKey`, manages async locks to dedupe concurrent fetches. Lives in `Koan.Cache` and is injected into stores that opt-in.
- `CacheConsistencyMode` enum + `CacheConsistencyOptions` allowing passthrough-on-failure, stale-while-revalidate, or strict modes.

### Public Surface Goals

- All async, cancellation-first signatures (`ValueTask` where practical).
- No provider-specific metadata leaks; everything flows through `CacheCapabilities` and `CacheEntryOptions`.
- Fluent API remains allocation-light by reusing builders via pooled objects (optional optimization after MVP).

## Adapter Construction Details

### In-Memory Adapter

- Register via `services.AddKoanCacheAdapter("memory")`. Under the hood:
  - Ensure `services.AddMemoryCache()` is present (throw guided exception otherwise).
  - Wrap `IMemoryCache` to implement `ICacheStore`.
  - Capabilities: `SupportsBinary = true`, `SupportsPubSubInvalidation = false`, `SupportsCompareExchange = true` (via `MemoryCacheEntryExtensions.SetPostEvictionCallback` + `lock`), `SupportsRegionScoping = true`.
  - Implement tagging using `ConcurrentDictionary<string, HashSet<string>>` guarded by `ReaderWriterLockSlim`, with periodic garbage collection triggered by eviction callbacks.
  - Persist stale metadata inside `MemoryCache` entry using `CacheEnvelope.Metadata` (lazy delegate for stale fallback runtime check).

### Redis Adapter

- Register via `services.AddKoanCacheAdapter("redis")`. Responsibilities:
  - Consume `RedisCacheOptions` sourced from `Configuration.Read(cfg, "Cache:Redis:Configuration", ...)`.
  - Reuse `IDistributedCache` for base operations; supplement with `IConnectionMultiplexer` (optional) for advanced commands (Lua scripts, pub/sub, distributed locks).
  - Capabilities: `SupportsBinary = true`, `SupportsPubSubInvalidation = true`, `SupportsCompareExchange = true` (Lua-based CAS), `SupportsRegionScoping = true`.
  - Tagging: maintain Redis Set per tag (key `tag::<tag>::members`). TTL mirrors entry TTL via `EXPIRE`. When provider lacks set support, degrade to prefix scan fallback flagged in `CacheCapabilities.Hints`.
  - Publish invalidation via `SUBSCRIBE`/`PUBLISH` to channel `Cache:Redis:Channel` defaulting to `koan-cache`. Consumers register once per app instance.
  - Singleflight: Acquire lock key `sf::<cacheKey>` with `SET key value NX PX <timeout>` before executing value factory. Fallback: if lock unavailable, await pub/sub invalidation or short delay before stale read.
    - Compare-and-exchange support is part of the MVP: use Lua (or `StringSet` with `When`) to enforce atomic swaps, and cover hot-path contention with integration tests before release.

### Testing Plan

- Memory adapter: deterministic unit tests verifying TTL, tagging, stale policy, singleflight concurrency using `Parallel.ForEachAsync` harness.
- Redis adapter: integration harness leveraging existing Koan container scripts (`scripts/module-inventory.ps1` already starts Redis for tests). Validate CAS, tag enumeration, invalidation fan-out.
- Provide `ICacheStore` contract tests to ensure any future adapters demonstrate parity via trait-based xUnit suite.

## Framework Integration Hooks

We insert caching through existing Koan touchpoints—no ad-hoc duplication.

| Pipeline                                   | Hook Type                      | Location             | Behavior                                                                                                                                                      |
| ------------------------------------------ | ------------------------------ | -------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Entity static methods (`Entity<T>`)        | `EntityQueryInterceptor` (new) | `Koan.Data.Core`     | Wraps `All`, `Query`, `Get`, `FirstPage` to consult cache when `[CachePolicy]` present. Uses metadata from `CachePolicyRegistry` constructed at startup.      |
| Entity instance methods (`Save`, `Delete`) | `EntityLifecycleEvents`        | `Koan.Data.Core`     | On `AfterSave`/`AfterDelete`, evict keys derived from policy (key template, tags).                                                                            |
| `EntityController<T>`                      | `CachePolicyFilter`            | `Koan.Web`           | MVC filter inspects controller or action `[CachePolicy]` annotations. For GET actions, returns cached response if available; for mutations, invalidates tags. |
| Data commands (instruction execution)      | `IDataInstructionInterceptor`  | `Koan.Data.Core`     | Exposes extension point for lower-level caching (e.g., raw queries) so adapters can short-circuit repeated instructions.                                      |
| Canon pipelines                            | `CachePipeline` (new)          | `Koan.Canon.Core`    | Provides scoped cache for enrichment steps, automatically respecting policy tags defined on source entities.                                                  |
| Background jobs / Koan. Orchestration      | `TaskEnvelope` metadata        | `Koan.Orchestration` | Allows job payloads to declare temporary cache usage and rely on distributed invalidation for multi-instance coordination.                                    |

### Policy Registry and Discovery

- `CachePolicyAttribute` attaches to entities, controllers, or methods. During `AddKoanCache()` we scan assemblies listed in auto-registrar manifest (same approach as existing capability registration).
- Discovered policies create entries inside `CachePolicyRegistry` keyed by `Type`/`MemberInfo`. This registry exposes:
  - `CacheKeyTemplate`: e.g., `"todo:{Id}"` or `"todo:list:{Completed}"`.
  - `CacheScope`: `Entity`, `Controller`, `Query`, `Response`.
  - `CacheStrategy`: `GetOrSet`, `SetOnly`, `InvalidateOnly`, etc.
  - `Consistency`: `Strict`, `StaleWhileRevalidate`, `PassthroughOnFailure`.

### Policy Attributes & Metadata

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public sealed class CachePolicyAttribute : Attribute
{
     public CachePolicyAttribute(CacheScope scope, string keyTemplate)
          => (Scope, KeyTemplate) = (scope, keyTemplate);

     public CacheScope Scope { get; }
     public string KeyTemplate { get; }
     public string[] Tags { get; init; } = Array.Empty<string>();
     public CacheStrategy Strategy { get; init; } = CacheStrategy.GetOrSet;
     public CacheConsistencyMode Consistency { get; init; } = CacheConsistencyMode.StaleWhileRevalidate;
     public TimeSpan? AbsoluteTtl { get; init; }
     public TimeSpan? SlidingTtl { get; init; }
     public bool ForcePublishInvalidation { get; init; }
}
```

- `CacheScope` enumeration examples: `Entity`, `EntityQuery`, `ControllerAction`, `ControllerResponse`, `PipelineStep`.
- `CacheStrategy` enumeration examples: `SetOnly`, `GetOnly`, `GetOrSet`, `Invalidate`, `NoCache` (explicit opt-out overriding parent policies).
- When applied to `Entity<T>`, the policy can declare `KeyTemplate = "todo:{Id}"` plus tags like `"todo"`, `"tenant:{TenantId}"`.
- When applied to controllers, we optionally include `KeyTemplate = "api/{Route}/{UserId}"`; tokens resolved via `CacheKeyInterpolation` dictionary (similar to `EntityRouteDefinition`).

Configuration-bound policies hydrate from strongly typed options (`CacheOptions`, `CachePolicyOptions`) and merge into the registry. Precedence is deterministic: global defaults feed attribute metadata, and explicit configuration overrides win when conflicts arise. Diagnostics emit the reconciled view so operators can confirm which source supplied each policy value.

### Insertion Mechanics

- `Entity<T>`: extend existing `EntityPipeline` to check `CachePolicyRegistry.TryGetQueryPolicy(typeof(T))`. If present, wrap provider call in `Cache.WithRecord<T>(resolvedKey)` before hitting data store. Use Koan's singleflight to avoid duplicate fetches. Save/delete hooks call `Cache.RemoveAsync` and `Cache.WithJson(...).Tag(...)` invalidation helpers.
- `EntityController<T>`: register MVC filter (`CachePolicyFilter`) at `AddKoanCache()` time. Filter queries `CachePolicyRegistry` for controller/action; if `Strategy == GetOrSet`, attempt retrieval before invoking action; otherwise set/invalidate post-execution. Filter ensures idempotency by keying on `HttpContext.RequestAborted` as cancellation token.
- `Data:InstructionExecution`: create `CacheInstructionInterceptor` implementing new `IDataInstructionInterceptor` contract. Interceptor examines instruction metadata, maps to cache key template (if `CachePolicyAttribute` is annotated on the instruction handler), and performs caching accordingly. Prevent duplication by routing through `CacheClient` rather than reimplementing per module.
- `Canon`: when `CachePolicyAttribute` indicates pipeline usage, `CachePipeline` obtains scope from canonical entity type and attaches to `CanonContext.Properties`. Steps access via `CachePipeline.Current` (pattern mirrors `KoanEnv`).

### Mutation Invalidation Patterns

| Surface                | Operation(s)                         | Eviction behaviour                                                                                                     |
| ---------------------- | ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------- |
| `Entity<T>` instances  | `Save()` (create/update), `Delete()` | `EntityLifecycleEvents.AfterSave/AfterDelete` resolve the policy key template, remove the concrete key, and flush tags |
| `EntityController<T>`  | `POST/PUT/PATCH/DELETE` actions       | `CachePolicyFilter` translates route data into cache keys/tags and evicts immediately after the action completes        |
| Instruction handlers   | Custom upserts/deletes               | `CacheInstructionInterceptor` consumes handler metadata, invokes `Cache.RemoveAsync`, and optionally clears tag sets    |

Upserts piggyback on `Entity<T>.Save()`, so the same lifecycle hook covers both create and update paths without extra wiring. Tag-based policies ensure related list queries or projections fall out of cache alongside the primary key entry. Future adapters must exercise mutation tests to verify that compare-and-exchange flows still trigger the eviction pipeline when writes succeed.

## Koan DX Enhancements & Opportunities

- **Auto key interpolation**: Provide `CacheKeyTemplateParser` that supports tokens like `{Id}` or `{User.Claims["tenant"]}`. Entities and controllers supply token material via `ICacheKeyContextProvider` interface (optional). Prevents repeated string interpolation code.
- **Declarative invalidation sets**: `[CachePolicy(Tags = new[]{"todo"}, Strategy = CacheStrategy.Invalidate)]` on mutation actions ensures consistent invalidation logic across commands.
- **Rate limiting synergy**: integrate with existing `RateLimitAttribute` by sharing the same cache adapter (Redis), avoiding multiple connection pools.
- **Diagnostics integration**: hook into the shared Koan diagnostics pipeline (no bespoke endpoint). `CacheOptions.EnableDiagnosticsEndpoint` gates registration so the existing diagnostics UI/lens lists policies, capability matrices, and hit/miss counters.
- **CLI support**: extend Koan CLI to emit baseline cache config and scaffold policy attributes for entities—it already scans entity metadata; we reuse that pipeline to avoid duplication.
- **Recipe integration**: update recipe templates (`Koan.Recipe.Abstractions`) so new services automatically call `services.AddKoanCache()` when `Cache:Provider` exists in environment manifest.

## Implementation & Delivery Plan (Detailed)

1. **Contracts & Options**

   - Create `Koan.Cache.Abstractions` with interfaces, enums, `CachePolicyAttribute`, `CacheCapabilities`, `CacheEntryOptions`.
   - Add unit tests verifying attribute defaults and options merge behavior.

2. **Core Module**

   - Implement `CacheClient`, `CacheEntryBuilder<T>`, `CacheScope`, `CachePolicyRegistry`, `CacheSingleflightRegistry`.
   - Wire DI via `AddKoanCache()` extension; register health checks, instrumentation, MVC filter provider.
   - Provide configuration binding classes (`CacheOptions`, `RedisCacheOptionsExtended`, `MemoryCacheAdapterOptions`).

3. **Adapters**

   - Memory adapter: implement `MemoryCacheStore`, hooking eviction callbacks to propagate tag cleanup. Document limitations (single-instance only).
   - Redis adapter: implement `RedisCacheStore`, bundling configurable channel name, script cache for CAS, optional cluster mode support.
   - Both adapters satisfy common test suite defined in `Koan.Cache.Tests`.

4. **Pipeline Hooks**

   - Extend `Koan.Data.Core` with `EntityQueryInterceptor` and `CacheInstructionInterceptor` referencing `ICacheClient` via DI.
   - Add MVC filter factory to `Koan.Web` for `CachePolicyFilter`.
   - Provide `CachePipeline` helper in `Koan.Canon.Core` enabling canonical operations to opt-in without direct adapter knowledge.

5. **Samples & Docs**

   - Update `S2.Api` (API sample) to demonstrate `[CachePolicy(CacheScope.ControllerAction, "api/todos/{RouteValues[id]}")]` for GET endpoints.
   - Update `S8.Canon` to cache canonicalization snapshots between processing steps.
   - Author `docs/decisions/ARCH-00XX-koan-cache-module.md` capturing final contract.

6. **Validation & DX Feedback**
   - Run docs lint + unit/integration test suites.
   - Provide developer preview instructions via `docs/guides/cache-policies-howto.md` (future deliverable).

## Downstream Module Adoption Targets

The caching module should light up multiple Koan pillars. The table below highlights the first wave we plan to integrate, mapped to their insertion hooks.

| Module                                          | Primary Gains                                                                                                            | Entry Point                                                   |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------- |
| `Koan.Data.Core`                                | Entity snapshot reuse, query memoization, paging metadata caching.                                                       | `EntityQueryInterceptor`, entity lifecycle events.            |
| `Koan.Canon.Core`                               | Canonicalization intermediate caching, dedupe of enrichment fetches, multi-instance coordination via Redis invalidation. | `CachePipeline` scope, Canon step helpers.                    |
| `Koan.Web` (`EntityController<T>`, MVC filters) | Response caching for GET, rate-limit counters, ETag generation using shared adapters.                                    | `CachePolicyFilter`, action-level `[CachePolicy]`.            |
| `Koan.Messaging.Core`                           | Message dedupe, outbox/inbox replay state, consumer cursor caching to reduce provider round-trips.                       | Messaging envelope interceptors and `InboxProcessor`.         |
| `Koan.Orchestration` (Aspire, CLI)              | Cache environment manifests, provisioned resource descriptors, capability snapshots for dashboards.                      | Auto-registrar boot metadata, orchestration manifest loaders. |
| `Koan.AI`                                       | Cache model catalogs, token price tables, safety configs, grounding context payloads.                                    | AI engine selection pipeline, prompt augmentation helpers.    |
| `Koan.Storage`                                  | Cache signed URLs, variant lookup tables, policy manifests to avoid repeated storage calls.                              | Storage profile resolution and `StorageRouter`.               |
| `Koan.Media.Core`                               | Cache transformation recipes, variant manifests, CDN origin metadata with tag-based invalidation.                        | Media pipeline orchestrators.                                 |
| `Koan.Recipe.Abstractions`                      | Speed up template scaffolding by caching generated manifests and detection results.                                      | Recipe execution context + CLI integrations.                  |
| `Koan.Diagnostics` / `Koan.Logging`             | Persist boot reports, capability inventories, health summaries for quick UI rendering.                                   | Diagnostics snapshot collectors.                              |

Each adoption target consumes the centralized `CachePolicyRegistry` and `Cache` fluent helpers to avoid duplicating caching logic per module.

## Migration & Adoption Plan

## Adapter & Capability Model

`Koan.Cache.Abstractions` surfaces capabilities so higher layers know what features are safe to use.

```csharp
public sealed record CacheCapabilities(
    bool SupportsBinary,
    bool SupportsPubSubInvalidation,
    bool SupportsCompareExchange,
    bool SupportsRegionScoping,
    IReadOnlySet<string> Hints);

public interface ICacheStore
{
    string ProviderName { get; }
    CacheCapabilities Capabilities { get; }
    ValueTask CacheAsync(CacheItemDescriptor descriptor, CancellationToken ct);
    ValueTask<CacheResult> FetchAsync(CacheItemDescriptor descriptor, CancellationToken ct);
    ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct);
    IAsyncEnumerable<TaggedCacheKey> EnumerateByTagAsync(string tag, CancellationToken ct);
}
```

Adapters implement `ICacheStore` by leaning on Koan data connectors:

- **Redis:** Uses `Koan.Data.Connector.Redis` for connection pooling, metrics, capability detection.
- **InMemory:** Lightweight, single-node implementation for development and tests (`Koan.Cache.Adapter.Memory`).
- **Couchbase/Hazelcast:** Reuse existing Koan connectors to provide distributed caching and region scoping.

Capabilities allow higher layers to branch without unsafe assumptions:

```csharp
var cache = Cache.Client;
if (cache.Store.Capabilities.SupportsPubSubInvalidation)
{
    await Cache.WithJson("profile:" + userId)
        .PublishInvalidation()
        .SetAsync(profileSnapshot, ct);
}
else
{
    await Cache.WithJson("profile:" + userId)
        .SetAsync(profileSnapshot, ct);
    await BackgroundRefresh.EnqueueAsync(userId, ct);
}
```

## Serialization Strategy

- Default serializer: Koan's `Koan.Core.Serialization.Json` for JSON payloads (camelCase, ISO-8601 dates).
- Binary payloads accept `ReadOnlyMemory<byte>` or `Stream` and skip serialization.
- Developers can register additional serializers by tagging implementations of `ICacheSerializer` with content-type metadata.
- Cache metadata (TTL, tags, region) is stored alongside each entry in a provider-agnostic envelope to ensure consistent invalidation semantics.

## Integration With Koan Data Layer

- The cache module sits parallel to `Koan.Data.Core`, but adapters map onto the same provider registration system. A provider can support both data persistence and caching (e.g., Redis) or only one.
- Entity methods can opt-in to caching via opt-in policies:

```csharp
public static class TodoCache
{
    public static ValueTask<Todo?> GetCachedAsync(Ulid id, CancellationToken ct)
        => Cache.WithRecord<Todo>($"todo:{id}")
            .For(TimeSpan.FromMinutes(5))
            .GetOrAddAsync(_ => Todo.Get(id, ct), ct);
}
```

- Canon pipelines can request scoped caches from `CachePipeline` to stash enrichment intermediate results.
- Data adapters can advertise cache preferences through configuration: `Data:Providers:Primary -> postgres`, `Cache:Provider -> redis`.

## Operational Guardrails

- Metrics: `Koan.Cache` emits OpenTelemetry metrics (`koan.cache.hits`, `koan.cache.misses`, `koan.cache.latency`).
- Logging: structured logs include key prefixes only (never full keys) to avoid leaking secrets.
- Health checks: `AddKoanCache()` registers a health contributor that tests provider connectivity.
- Resilience: Circuit breakers wrap provider calls; backpressure triggers degrade gracefully to passthrough (skip cache) when providers fail.

## Files, Streams, and Embeddings

- `Cache.WithBinary(key)` streams via `PipeReader`/`PipeWriter` for efficient file caching.
- For vector embeddings, use `Cache.WithRecord<VectorSnapshot>(key)` with adapters guaranteeing byte-order preservation.
- Large file caching leverages chunked storage when provider supports it (Redis module for `CACHE.MSET` or object storage feeds paired with metadata keys).

## Migration & Adoption Plan

1. Ship `Koan.Cache` as an opt-in module alongside adapters for Redis and in-memory.
2. Update reference samples (`S2.Api`, `S8.Canon`) to use `Cache` helpers for rate limiting, session state, and Canon pipeline memoization.
3. Extend `Koan.CoreOnly.slnf` to include the cache module once implementation lands.
4. Document policy precedence and diagnostics visibility in the architecture docs and `CachePolicyRegistry` implementation notes.
5. Land Redis CAS integration tests covering compare-and-exchange contention paths.
6. Fold cache telemetry into the shared diagnostics surface, controlled via `CacheOptions.EnableDiagnosticsEndpoint`.
7. Long-term: unify rate-limiting, distributed locking, and task coordination atop cache providers.

## Next Steps

1. Prototype `ICacheStore` contracts and register them in `Koan.Core` previews.
2. Implement adapter discovery that reuses Koan data provider registration metadata.
3. Wire policy merge precedence into `CachePolicyRegistry`, ensuring diagnostics can surface the final configuration source map.
4. Land Redis CAS integration tests that exercise Lua-based compare-and-exchange paths under contention.
5. Fold cache telemetry into the shared diagnostics module and document enablement via `CacheOptions`.
6. Extend docs with deployment guidance (e.g., Redis cluster sizing, in-memory fallback for tests).
7. Track CLI scaffolding as a follow-up item post-MVP (deferred).
