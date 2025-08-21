# DATA-0057: Core Singleflight for In-Flight Deduplication

- Status: Accepted
- Date: 2025-08-20
- Authors: Data & Core

## Context

Relational adapters (SQLite, SQL Server, Postgres) ensure and validate schema on demand. When multiple concurrent requests hit a new entity/table, each adapter could attempt orchestration simultaneously, causing duplicate work and occasional races. We previously used ad-hoc per-adapter locks and caches.

## Decision

Introduce a generic singleflight utility in Core: `Sora.Core.Infrastructure.Singleflight`.

- Dedupe by key (string).
- Only in-flight deduplication: entries are removed after completion; no TTL/state retention.
- Async API:
  - `Task RunAsync(string key, Func<CancellationToken, Task> work, CancellationToken ct = default)`
  - `Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> work, CancellationToken ct = default)`
  - `void Invalidate(string key)` to remove an in-flight entry if present (safe no-op otherwise).

Relational adds a thin shim `Sora.Data.Relational.Infrastructure.Singleflight` forwarding to Core for convenience/back-compat.

SQLite, SQL Server, and Postgres adapters integrate this to dedupe ensure/validate/DDL per database+table key.

## Rationale

- Centralization: singleflight is broadly useful (data adapters, messaging, web composition). Core is the right home.
- Simplicity: avoids per-adapter lock dictionaries and reduces fragile concurrency code.
- Safety: dedupes only concurrent operations; steady-state guard remains the adapter-level healthy cache.

## Consequences

- Adapters can opt-in with one line and a sensible key.
- No behavioral change for non-concurrent callers.
- Errors propagate to all awaiting callers; callers should handle exceptions appropriately.
- Not a cache: follow-up calls after completion will execute again unless prevented by the adapter's healthy cache.

## Keying convention

- Use a stable resource key including the database name when available, e.g.:
  - SQLite: `"{DataSource}/{Database}::{TableName}"`
  - SQL Server: `"{DataSource}/{Database}::{TableName}"`
  - Postgres: `"{Host}/{Database}::{TableName}"`
- For other domains, pick a key that uniquely identifies the protected operation scope.

## Alternatives considered

- Global locks (risk deadlocks, coarse granularity).
- SemaphoreSlim per-key (similar usage but needs custom lifecycle/cleanup).
- TTL memoization (not needed here; we only want in-flight dedupe).

## Adoption guidance

- Wrap the narrow critical section (validate+ensure path) inside `Singleflight.RunAsync`.
- Retain the steady-state healthy/ready cache to avoid re-running on subsequent requests.
- Use `Invalidate(key)` when upstream invalidates the resource (rarely needed for ensures).

## Implementation notes

- Singleflight is implemented with `ConcurrentDictionary<string, Lazy<Task>>` and `ExecutionAndPublication` to ensure only one work factory runs per key.
- Entries are removed in a finally block to avoid memory growth.
- Relational shim keeps existing imports working; new code should prefer the Core namespace.

## Usage examples

Minimal pattern inside an adapter ensure path:

```csharp
var key = $"{conn.DataSource}/{conn.Database}::{table}"; // or Host/Database for Npgsql
await Singleflight.RunAsync(key, async ct =>
{
  // validate current state
  var report = (IDictionary<string, object?>)await orchestrator.ValidateAsync<TEntity, TKey>(ddl, features, ct);
  var ddlAllowed = report.TryGetValue("DdlAllowed", out var da) && da is bool b && b;
  var tableExists = report.TryGetValue("TableExists", out var te) && te is bool tb && tb;
  if (ddlAllowed)
  {
    await orchestrator.EnsureCreatedAsync<TEntity, TKey>(ddl, features, ct);
    healthyCache[key] = true; return;
  }
  if (tableExists) { healthyCache[key] = true; }
});
```

Invalidating after a schema clear:

```csharp
await conn.ExecuteAsync($"DROP TABLE ...");
healthyCache.TryRemove(key, out _);
```

## Status & Verification

- Integrated in SQLite, SQL Server, and Postgres adapters; all adapter test suites are green.
- Relational shim compiles and forwards to Core.
