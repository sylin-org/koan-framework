---
type: REF
domain: data
title: "Koan Cache Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-06
framework_version: v0.6.3
validation:
  status: not-yet-tested
  scope: docs/reference/data/cache.md
---

# Koan Cache Reference

## Contract

- **Inputs**: Koan application bootstrapped with `builder.Services.AddKoan()` and `AddKoanCache(...)`, at least one cache adapter package (memory or Redis), and familiarity with `Entity<TEntity>` patterns.
- **Outputs**: Repeatable caching flows using fluent builders, tag-driven invalidation, policy-based entity helpers, and provider instrumentation aligned with Koan conventions.
- **Error Modes**: Missing adapter registration (`AddKoanCacheAdapter(...)`), cache providers lacking advertised capabilities (pub/sub, tagging), mis-specified key templates, and long-running factories that time out the singleflight gate.
- **Success Criteria**: Keys resolve consistently across instances, stale-while-revalidate behaves predictably, tag invalidation removes the intended entries, and cache metrics surface through Koan telemetry.

### Edge Cases

1. **Provider capability gaps** – Always inspect `ICacheClient.Store.Capabilities` before relying on tags or pub/sub invalidation.
2. **Ambient scopes** – Nested `Cache.BeginScope` calls must be disposed in order; mismatched pops throw to prevent cross-tenant leakage.
3. **Factory fan-out** – Value factories should be idempotent and short-lived; singleflight timeouts fall back to executing the factory concurrently.
4. **Placeholder tags** – Tag templates containing `{}` placeholders are ignored by `Entity<TEntity,TKey>.Cache`; provide concrete tags when flushing.
5. **Serializer selection** – Choosing `WithBinary` or `WithJson` impacts payload size and serialization; streaming large blobs through JSON can exceed provider limits.

---

## Overview

`Koan.Cache` provides a fluent cache client, policy registry, tagging utilities, and instrumentation. It follows the same "reference = intent" philosophy as entities: declare your desired consistency, then let Koan handle provider selection and telemetry.

- **Core package**: `Koan.Cache`
- **Adapters**: `Koan.Cache.Adapter.Memory`, `Koan.Cache.Adapter.Redis`
- **Abstractions**: `Koan.Cache.Abstractions`

All adapters auto-register through `AddKoanCacheAdapter("<provider>")`, mirroring the data pillar.

---

## Quick Start

1. Reference the packages:
   - `Koan.Cache`
   - `Koan.Cache.Adapter.Redis` (or `Koan.Cache.Adapter.Memory`)
2. Enable caching inside your auto-registrar:

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S1.Web";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCache(configuration: null, configure: opts =>
        {
            opts.DefaultSingleflightTimeout = TimeSpan.FromSeconds(3);
        });

        services.AddKoanCacheAdapter("redis");
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion)
              .AddCapability("cache-provider", cfg["Cache:Provider"] ?? "memory");
    }
}
```

3. Configure the provider (defaults to memory if omitted):

```json
{
  "Cache": {
    "Provider": "redis",
    "Redis": {
      "Configuration": "localhost:6379",
      "ChannelName": "koan-cache",
      "InstanceName": "s1",
      "EnablePubSubInvalidation": true,
      "EnableStaleWhileRevalidate": true
    }
  }
}
```

Environment overrides follow the same hierarchy, for example:

```bash
export Cache__Provider=memory
export Cache__Redis__Configuration="prod-cache:6379"
```

---

## Fluent API Basics

The static `Cache` facade resolves the scoped `ICacheClient` and returns an `ICacheEntryBuilder<T>` tuned for the chosen content kind.

```csharp
// Cache a computed list of open todos for one minute.
var todos = await Cache.WithJson<Todo[]>("todo:open:v1")
    .WithAbsoluteTtl(TimeSpan.FromMinutes(1))
    .WithTags("todo", "tenant:1001")
    .GetOrAddAsync(async ct => await Todo.Query(t => !t.Completed, ct), ct);
```

Available builder helpers:

| Helper | Description |
| --- | --- |
| `WithJson<T>(key)` / `WithRecord<T>(key)` | JSON serialization for objects and record types. |
| `WithString(key)` | UTF-8 string storage. |
| `WithBinary(key)` | Raw byte arrays or streams. |
| `WithAbsoluteTtl(ttl)` / `WithSlidingTtl(ttl)` | Expiration control. |
| `AllowStaleFor(duration)` | Enables stale-while-revalidate when the adapter supports it. |
| `PublishInvalidation()` | Forces pub/sub messages for Redis-style adapters. |
| `WithTags(params string[] tags)` | Normalizes and deduplicates tags for selective flushes. |

To probe without fetching the payload, call `await Cache.Exists("todo:open:v1", ct);`.

---

## Tag-Based Invalidation

Use the fluent tag set for ad-hoc invalidation:

```csharp
var flushed = await Cache.Tags("todo", "tenant:1001").Flush(ct);
```

- `Flush` returns the number of keys removed.
- `Count` reports unique keys under the supplied tags.
- `Any` short-circuits to `false` when no tags resolve, minimizing store hits.

Tag operations rely on provider support. Redis maintains tag sets via sorted sets; the memory adapter keeps a thread-safe index with entry TTL alignment.

---

## Entity Cache Helpers

`Entity<TEntity, TKey>.Cache` bridges cache policies to the entity surface. Policies gather tags and key templates declared via `[CachePolicy]`.

```csharp
[CachePolicy(CacheScope.Entity, "todo:{Id}", Tags = new[] { "todo", "tenant:{TenantId}" })]
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
    public string TenantId { get; set; } = "";

    public static Task<Todo[]> Open() => Query().Where(t => !t.Completed);
}

// Flush all cached todos for the current tenant.
await Todo.Cache.Flush(new[] { $"tenant:{tenantId}" }, ct);

// Diagnostics
var cachedCount = await Todo.Cache.Count(ct);
var anyCached = await Todo.Cache.Any(new[] { "todo" }, ct);
```

Placeholders inside tag templates are filtered out during flush operations unless you provide concrete values (e.g., `tenant:{TenantId}` requires a runtime `tenant:123`).

---

## Policy-Driven Caching

Decorate entities, controllers, or specific methods with `[CachePolicy]` to describe cache intent. The `CachePolicyRegistry` scans assemblies at startup and the `CacheRepositoryDecorator` applies caching to repositories automatically.

```csharp
[CachePolicy(
    scope: CacheScope.Entity,
    keyTemplate: "todo:{Id}",
    Tags = new[] { "todo", "tenant:{TenantId}" },
    ForcePublishInvalidation = true)]
public class Todo : Entity<Todo>
{
    public static Task<Todo?> ByTitle(string title) =>
        Query().Where(t => t.Title == title).FirstOrDefaultAsync();
}
```

> **Key template tokens** – `${Id}`, `${Key}`, and property paths (e.g., `{Entity.TenantId}`) resolve dynamically. Missing tokens prevent caching for the current call and emit a debug log entry.

Repositories picked up by Koan’s data infrastructure are wrapped by `CachedRepository<TEntity,TKey>` when a matching policy exists. Strategies include:

- `GetOrSet` (default) – read-through with write-back on mutations.
- `GetOnly` – read cache but do not populate missing entries.
- `SetOnly` – push writes without read-through.
- `Invalidate` – flush entries after mutations without reading.

---

## Singleflight & Stale Strategies

`CacheSingleflightRegistry` deduplicates concurrent work by key. The default timeout comes from `CacheOptions.DefaultSingleflightTimeout` (2s by default).

```csharp
var report = await Cache.WithJson<UsageReport>("report:tenant:1001")
    .AllowStaleFor(TimeSpan.FromMinutes(10))
    .GetOrAddAsync(async innerCt => await UsageReport.BuildAsync(innerCt), ct);
```

- **Singleflight**: prevents the `BuildAsync` work from running multiple times in parallel per key.
- **AllowStaleFor**: serves stale data during the revalidation window when adapters support it (Redis & memory adapters do).

If the factory exceeds the timeout, additional callers execute their own factories; stale responses remain available until eviction. When an adapter does not advertise `singleflight`, the registry skips lock acquisition and behaves as pass-through.

---

## Instrumentation & Capabilities

`CacheInstrumentation` publishes counters via `Meter("Koan.Cache", "0.6.3")`:

| Counter | Description |
| --- | --- |
| `koan.cache.hits` | Cache hits per provider |
| `koan.cache.misses` | Cache misses per provider |
| `koan.cache.sets` | Successful writes |
| `koan.cache.removes` | Explicit removals |
| `koan.cache.invalidations` | Pub/sub invalidations published |

Inspect adapter capabilities at runtime:

```csharp
var caps = Cache.Client.Store.Capabilities;
if (!caps.SupportsPubSubInvalidation)
{
    _logger.LogWarning("Pub/Sub invalidation disabled for {Provider}", Cache.Client.Store.ProviderName);
}
```

Hints surface optional behaviors such as `singleflight` or `tags`.

---

## Provider Notes

### Memory Adapter

- Default when `Cache:Provider` is unset.
- Stores entries in-process using `IMemoryCache`.
- Supports binary payloads, tagging, and stale-while-revalidate.
- No cross-instance invalidation.

### Redis Adapter

- Requires StackExchange.Redis and reachable Redis host.
- Maintains per-tag sets and publishes invalidations to `Cache:Redis:ChannelName`.
- Honors `AllowStaleFor` by storing stale deadlines alongside the entry envelope.
- Enumerates tagged keys via `SetScan`; expired keys are pruned lazily.

---

## Related Reading

- [Data Pillar Reference](./index.md)
- [Koan Cache Module Architecture](../../architecture/koan-cache-module.md)
- [Entity Lifecycle Events](./entity-lifecycle-events.md)
- [Modules Reference](../modules-overview.md)
