# Findings and Recommendations (2025-10-07)

---
type: GUIDE
domain: data
title: "Entity Capabilities How-To"
audience: [developers, architects]
status: draft
last_updated: 2025-10-02
framework_version: v0.6.3
validation:
    status: not-yet-tested
    scope: docs/guides/entity-capabilities-howto.md
---

This section summarizes best practices and recommendations for maximizing the value of Koan's entity and automation features, based on a deep review of current and planned usage in sample applications like PantryPal.

## Key Koan Features to Prefer

- **Entity<T> Patterns:** Use static and instance methods (`All`, `Get`, `Save`, `Remove`, `Page`, `Query`, etc.) for all data access. Prefer `EntityController<T>` for CRUD APIs to minimize boilerplate and maximize consistency.
- **Batch Retrieval:** Use `Entity.Get(IEnumerable<TKey>)` to fetch multiple entities efficiently in a single query. Returns results with preserved order and nulls for missing entities, avoiding N+1 query problems.
- **Querying, Pagination, and Streaming:** Use `Page`, `FirstPage`, and `QueryStream`/`AllStream` for large datasets and web UI pagination. Use `QueryWithCount` for efficient UI pagination controls. Use LINQ and string queries for flexible filtering.
- **Batch Operations:** Use `List<T>.Save()` and `Entity.Batch()` for bulk persistence and updates. Leverage lifecycle hooks (`BeforeUpsert`, `AfterLoad`, etc.) for validation and projections.
- **Bulk Removal Strategies:** Use `RemoveAll()` with the appropriate strategy (`Safe`, `Fast`, `Optimized`) for test cleanup, production, or audit scenarios.
- **Context Routing:** Use `EntityContext.Partition`, `EntityContext.Source`, and `EntityContext.Adapter` for multi-user, analytics, or provider-specific routing.
- **Transaction Coordination:** Use `EntityContext.Transaction(name)` to coordinate entity operations across multiple adapters with best-effort atomicity. Auto-commit on dispose for minimal cognitive load.
- **Provider Capabilities:** Check and adapt to provider capabilities (e.g., `SupportsFastRemove`, LINQ support) for optimal performance and compatibility.
- **Pipelines and Jobs:** Use Koan Flow pipelines and Jobs for orchestrating multi-step or background operations (e.g., media processing, batch imports).
- **Self-Reporting and Diagnostics:** Use Koan’s self-reporting features to surface provider elections, capabilities, and diagnostics in logs or UI.

## Application Guidance

- Use batch retrieval (`Entity.Get(ids)`) instead of loops with individual Gets to avoid N+1 query problems.
- Refactor all in-memory queries to use `Page`, `QueryStream`, or `AllStream`.
- Use `EntityController<T>` for all CRUD endpoints.
- Implement batch operations and lifecycle hooks for bulk updates and validation.
- Use context routing for user-specific or analytics scenarios.
- Use `EntityContext.Transaction(name)` for multi-step operations requiring atomicity across entities or adapters.
- Leverage provider capability checks to optimize for the current backend.
- For media and vision pipelines, use Koan Jobs or Flow for background/cascading work.
- Document and demonstrate these patterns in both code and developer docs.

**Summary:**
Rely on Koan’s entity, controller, pipeline, and job abstractions for all core behaviors. Avoid custom repositories, manual paging, or ad-hoc background work. Use the patterns and recipes in this guide as the canonical reference for all new features and documentation.
<!-- Front-matter normalized above; removed duplicate block. -->

# Koan Entity Capabilities: End-to-End Guide

This guide walks through the Koan data pillar, from a single `todo.Save()` to multi-provider ---

## Next Steps

Extend the transfer DSL in your domain by adding `.Mirror()` runs before cut-overs, or `Copy()` recipes to hydrate analytics sources. Combine Flow and Jobs with the transfer DSL to orchestrate large data migrations safely. For working examples, see the `samples/S5.Recs` project.

When in doubt, stick to the entity-first patterns above. They keep your code declarative, provider-agnostic, and ready for Koan's automation pillars., Flow pipelines, and vector exports. Each section grows in sophistication, covering concepts, required packages and configuration, and usage scenarios.

## Prerequisites

Add the Koan baseline packages:

```xml
<PackageReference Include="Koan.Core" Version="0.6.2" />
<PackageReference Include="Koan.Data.Core" Version="0.6.2" />
<PackageReference Include="Koan.Data.Abstractions" Version="0.6.2" />
```

Reference at least one data adapter (SQLite example below):

```xml
<PackageReference Include="Koan.Data.Connector.Sqlite" Version="0.6.2" />
```

Optionally configure the default source in `appsettings.json` (Koan works without configuration, using sensible defaults):

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=app.db"
        }
      }
    }
  }
}
```

Boot the runtime with `builder.Services.AddKoan();` in `Program.cs`. Everything below builds on this foundation.

---

## 1. Foundations: Defining and Saving Entities

**Concepts**

`Entity<T>` provides auto GUID v7 IDs, instance methods (`Save`, `Remove`), and static helpers (`Get`, `All`, `RemoveAll`). Everything routes through the default source configured in app settings.

**Recipe**

Packages already listed in prerequisites. No special configuration needed.

**Sample**

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

var todo = new Todo { Title = "Plant strawberries" };
await todo.Save();          // Persists via default adapter

var fetched = await Todo.Get(todo.Id);
await fetched!.Remove();
```

**Batch Retrieval**

Koan provides efficient batch retrieval when you need to fetch multiple entities by ID. Returns results in the same order as input IDs, with null for missing entities.

```csharp
// Batch fetch by IDs - preserves order, returns nulls for missing
var ids = new[] { id1, id2, id3, id4 };
var todos = await Todo.Get(ids, ct);
// Result: [Todo?, Todo?, null, Todo?] - third ID not found

// Filter out nulls if needed
var foundTodos = todos.Where(t => t != null).Select(t => t!).ToList();

// Partition-aware batch retrieval
using (EntityContext.Partition("archive"))
{
    var archivedTodos = await Todo.Get(ids, ct);
}
```

**Performance Benefits**

Batch Get uses provider-optimized bulk queries instead of N individual requests:

```csharp
// ❌ Inefficient: N database round-trips
var todos = new List<Todo?>();
foreach (var id in collectionIds)
{
    todos.Add(await Todo.Get(id, ct));  // N queries
}

// ✅ Efficient: Single bulk query with IN clause
var todos = await Todo.Get(collectionIds, ct);  // 1 query
```

**Common Use Cases**

```csharp
// Collection/playlist pagination - fetch page of items by stored IDs
var collection = await Collection.Get(collectionId, ct);
var pageIds = collection.ItemIds.Skip(skip).Take(pageSize).ToList();
var items = await Item.Get(pageIds, ct);

// Relationship navigation - fetch all related entities
var order = await Order.Get(orderId, ct);
var orderItems = await OrderItem.Get(order.ItemIds, ct);

// Bulk validation - check which IDs exist
var requestedIds = GetRequestedIds();
var existing = await Todo.Get(requestedIds, ct);
var missingIds = requestedIds.Zip(existing)
    .Where(pair => pair.Second == null)
    .Select(pair => pair.First)
    .ToList();
```

**Usage Scenarios**

Applications efficiently load collection items or playlists by fetching a page of pre-ordered IDs in a single query. Relationship navigation loads multiple related entities without N+1 queries. Result order preservation ensures UI displays items in the intended sequence.

**Custom Keys**

```csharp
public class NumericTodo : Entity<NumericTodo, int>
{
    public override int Id { get; set; }
    public string Title { get; set; } = "";
}

await NumericTodo.RemoveAll();
await new NumericTodo { Id = 42, Title = "Meaningful" }.Save();
```

---

## 2. Querying, Pagination, Streaming

**Concepts**

LINQ methods (`Query`, `QueryWithCount`), string queries, paging helpers (`FirstPage`, `Page`). Streaming (`AllStream`, `QueryStream`) yields `IAsyncEnumerable` for large datasets. Providers that lack server-side LINQ fall back to client evaluation (Koan warns you).

**Recipe**

Same packages as foundations. Prefer adapters that implement `ILinqQueryRepository` (Postgres, Mongo) for server-side filters.

**Sample**

```csharp
var overdue = await Todo.Query(t => !t.Completed && t.DueDate < DateTimeOffset.UtcNow);
var secondPage = await Todo.Page(page: 2, size: 20);

await foreach (var reading in Reading.QueryStream("plot == "A1"", batchSize: 200, ct))
{
    Process(reading);
}
```

**Usage Scenarios**

Applications stream large datasets (logs, sensor readings, historical records) without exhausting memory. APIs paginate results while providing total counts for UI pagination controls.

**QueryWithCount and Options**

```csharp
var result = await Todo.QueryWithCount(
    t => t.ProjectId == projectId,
    new DataQueryOptions(orderBy: nameof(Todo.Created), descending: true),
    ct);

Console.WriteLine($"Showing {result.Items.Count} of {result.TotalCount}");
```

### Count Operations and Strategies

**Concepts**

Koan provides fast count operations with three strategies: Exact (full accuracy), Fast (metadata estimates), and Optimized (framework chooses). Progressive disclosure means `await Entity.Count` works immediately, while explicit control (`Count.Exact()`, `Count.Fast()`) appears when needed. Provider-specific optimizations deliver 1000x+ speedups for large tables using database metadata.

**Recipe**

Same packages as foundations. Providers with statistics metadata (Postgres, SQL Server, MongoDB) support fast counts. Providers without metadata (SQLite, Redis, JSON, InMemory) return exact counts for all strategies.

**Simple Counts**

```csharp
// Default: framework chooses best strategy (usually optimized)
var total = await Todo.Count;

// Explicit exact count (guaranteed accuracy, may be slower)
var exact = await Todo.Count.Exact(ct);

// Explicit fast count (metadata estimate, extremely fast)
var fast = await Todo.Count.Fast(ct);

// Filtered count with predicate
var completed = await Todo.Count.Where(t => t.Completed);

// Filtered with explicit strategy
var urgent = await Todo.Count.Where(t => t.Priority > 3, CountStrategy.Fast, ct);

// Count specific partition
var archivedCount = await Todo.Count.Partition("archive");
```

**Performance Characteristics**

| Provider   | Exact Count (10M rows) | Fast Count (10M rows)   | Strategy                              |
| ---------- | ---------------------- | ----------------------- | ------------------------------------- |
| PostgreSQL | ~25 seconds            | ~5ms (5000x faster)     | `pg_stat_user_tables.n_live_tup`      |
| SQL Server | ~20 seconds            | ~1ms (20000x faster)    | `sys.dm_db_partition_stats.row_count` |
| MongoDB    | ~15 seconds            | ~10ms (1500x faster)    | `estimatedDocumentCount()`            |
| SQLite     | Full scan              | Full scan (same)        | No metadata available                 |
| Redis      | O(n) scan              | O(n) scan (same)        | No metadata available                 |
| JSON       | In-memory count        | In-memory count (same)  | No metadata available                 |
| InMemory   | Dictionary count       | Dictionary count (same) | No metadata available                 |

**When to Use Each Strategy**

```csharp
// Pagination UI (fast is fine, exact unnecessary)
var page = await Todo.Page(pageNumber, pageSize);
var totalPages = (await Todo.Count.Fast(ct) + pageSize - 1) / pageSize;

// Dashboard summary (estimates acceptable)
var stats = new {
    TotalTodos = await Todo.Count.Fast(ct),
    Completed = await Todo.Count.Where(t => t.Completed, CountStrategy.Fast, ct),
    Pending = await Todo.Count.Where(t => !t.Completed, CountStrategy.Fast, ct)
};

// Critical business logic (exact required)
var exactInventory = await Product.Count.Where(p => p.InStock, CountStrategy.Exact, ct);
if (exactInventory < threshold)
    await NotifyRestock();

// Default optimized (framework decides)
var count = await Todo.Count; // Uses Fast if available, Exact otherwise
```

**Advanced: CountResult and IsEstimate**

For scenarios requiring awareness of estimate vs exact:

```csharp
// Access repository directly for CountResult
var repo = serviceProvider.GetRequiredService<IRepository<Todo, string>>();
var request = new CountRequest<Todo>();
var result = await repo.CountAsync(request, ct);

Console.WriteLine($"Count: {result.Value}");
if (result.IsEstimate)
    Console.WriteLine("⚠️ This is an estimate from database metadata");
else
    Console.WriteLine("✓ This is an exact count");

// Entity API returns long directly (convenience over precision awareness)
var simple = await Todo.Count; // Returns long, hides IsEstimate flag
```

**Usage Scenarios**

Applications display pagination controls using fast counts (sub-10ms) instead of blocking on full table scans. Dashboards refresh every few seconds with metadata estimates, reserving exact counts for critical reports. Analytics queries route fast counts to APIs while batch jobs use exact counts for reconciliation.

---

## 3. Batch Operations and Lifecycle Hooks

**Concepts**

`List<T>.Save()` provides bulk persistence. `Entity.Batch()` combines adds, updates, and deletes. Lifecycle hooks (`ProtectAll`, `BeforeUpsert`, `AfterLoad`) keep invariants and projections near the data.

**Recipe**

Dependencies already covered. Lifecycle API lives in `Koan.Data.Core.Events` (included with `Koan.Data.Core`).

**Sample**

```csharp
var todos = Enumerable.Range(0, 1000)
                      .Select(i => new Todo { Title = $"Seed {i}" })
                      .ToList();
await todos.Save();

await Todo.Batch()
          .Add(new Todo { Title = "Add me" })
          .Update(existingId, todo => todo.Completed = true)
          .Delete(oldId)
          .SaveAsync();
```

**Lifecycle Example**

```csharp
public static class TodoLifecycle
{
    public static void Configure(EntityLifecycleBuilder<Todo> builder) =>
        builder.ProtectAll()
               .Allow(t => t.Title, t => t.Completed)
               .BeforeUpsert(async (ctx, next) =>
               {
                   if (string.IsNullOrWhiteSpace(ctx.Entity.Title))
                       throw new InvalidOperationException("Title required");
                   await next();
               })
               .AfterLoad(ctx => ctx.Entity.DisplayTitle = ctx.Entity.Title.ToUpperInvariant());
}
```

**Usage Scenarios**

Domain models enforce required fields and format display text automatically. Batch imports process thousands of records in a single call to prep nightly jobs or migrations.

### Bulk Removal and Strategies

**Concepts**

Koan provides three removal strategies: **Safe** (always fires lifecycle hooks), **Fast** (always bypasses hooks for 10-250x performance), and **Optimized** (capability-based selection). Default `RemoveAll()` uses Optimized: selects Fast on providers with `WriteCapabilities.FastRemove` (Postgres, SQL Server, MongoDB, SQLite, Redis), Safe on others (JSON, InMemory, Couchbase). **Important**: Optimized bypasses hooks on most providers. Use explicit `RemoveStrategy.Safe` when audit trail is required.

**Quick Decision Guide:**
- **Audit trail required?** → Use `RemoveStrategy.Safe` (always fires hooks)
- **Test cleanup, temp data?** → Use `RemoveAll()` default (Optimized for speed)
- **Known no-audit scenario?** → Use `RemoveStrategy.Fast` (explicit bypass)

**Recipe**

Same packages as foundations. Providers with fast removal support (Postgres, SQL Server, MongoDB, SQLite, Redis) expose `WriteCapabilities.FastRemove`. Providers without fast paths (JSON, InMemory, Couchbase) silently fall back to Safe behavior.

**Simple Removal**

```csharp
// Default: Optimized strategy (framework chooses based on provider capabilities)
// Postgres/SQL Server/Mongo/SQLite/Redis: Uses Fast (TRUNCATE/DROP/UNLINK)
// JSON/InMemory/Couchbase: Uses Safe (no fast path available)
var deletedCount = await Todo.RemoveAll(ct);

// Explicit safe removal (always fires hooks, maintains audit trail)
// Use when you need audit trail even if provider supports fast removal
var deletedCount = await Todo.RemoveAll(RemoveStrategy.Safe, ct);

// Explicit fast removal (bypasses hooks, 10-250x faster)
// Use when you're certain hooks aren't needed
var deletedCount = await Todo.RemoveAll(RemoveStrategy.Fast, ct);

// Remove from specific partition with Optimized strategy
var archivedCount = await Todo.RemoveAll(RemoveStrategy.Optimized, "archive", ct);

// Remove using EntityContext (scoped to partition)
using (EntityContext.Partition("archive"))
{
    // Removes from "archive" partition using Optimized strategy
    // Provider automatically chooses Fast if supported
    await Todo.RemoveAll(ct);
}

// Check provider support for fast removal
if (Todo.SupportsFastRemove)
{
    Console.WriteLine("Provider supports TRUNCATE/DROP - Optimized will use Fast");
}
else
{
    Console.WriteLine("Provider lacks fast path - Optimized will use Safe");
}
```

**Performance Characteristics**

| Provider   | Safe (1M rows) | Fast/Optimized (1M rows) | Implementation | Optimized Uses |
| ---------- | -------------- | ------------------------ | -------------- | -------------- |
| PostgreSQL | ~45 seconds    | ~200ms (225x faster)     | `TRUNCATE TABLE RESTART IDENTITY` | **Fast** ⚡ |
| SQL Server | ~38 seconds    | ~150ms (253x faster)     | `TRUNCATE TABLE` | **Fast** ⚡ |
| MongoDB    | ~52 seconds    | ~300ms (173x faster)     | Drop + recreate indexes | **Fast** ⚡ |
| SQLite     | ~25 seconds    | ~2s (12.5x faster)       | `DELETE` + `VACUUM` | **Fast** ⚡ |
| Redis      | ~18 seconds    | ~800ms (22.5x faster)    | `UNLINK` (async) | **Fast** ⚡ |
| JSON       | ~50ms          | ~50ms (same)             | Dictionary clear | Safe |
| InMemory   | ~5ms           | ~5ms (same)              | Dictionary clear | Safe |
| Couchbase  | Standard       | Standard (same)          | DELETE query | Safe |

**Provider-Specific Semantics**

```csharp
// PostgreSQL/SQL Server: TRUNCATE resets identity counters
await Product.RemoveAll(RemoveStrategy.Fast, ct);
// Next insert starts at ID = 1 (identity/sequence reset)

// Foreign key constraints may block TRUNCATE (auto-fallback to DELETE)
await Order.RemoveAll(RemoveStrategy.Fast, ct);
// If Orders has FK references, automatically falls back to Safe DELETE

// MongoDB: Brief index loss during drop/recreate
await Media.RemoveAll(RemoveStrategy.Fast, ct);
// Collection dropped, recreated with same indexes (milliseconds)

// SQLite: VACUUM reclaims disk space
await Log.RemoveAll(RemoveStrategy.Fast, ct);
// DELETE + VACUUM shrinks database file

// Redis: UNLINK is non-blocking
await Session.RemoveAll(RemoveStrategy.Fast, ct);
// Keys unlinked immediately, memory freed in background thread
```

**When to Use Each Strategy**

```csharp
// ✅ Optimized (default): Best for non-audited bulk cleanup
[Fact]
public async Task BulkImportTest()
{
    // Optimized uses TRUNCATE on Postgres (fast), DELETE on InMemory (safe fallback)
    await Todo.RemoveAll(ct);
    var todos = GenerateTestData(1000);
    await todos.Save();
}

// ✅ Optimized: Development/staging environment resets
public async Task ResetEnvironment()
{
    // Automatically uses fastest method available (TRUNCATE/DROP)
    await Todo.RemoveAll(ct);
    await SeedData();
}

// ✅ Safe: Production operations requiring audit trail
public async Task DeleteTenant(string tenantId)
{
    using (EntityContext.Partition($"tenant-{tenantId}"))
    {
        // Explicit Safe guarantees audit hooks fire on ALL providers
        await Order.RemoveAll(RemoveStrategy.Safe, ct);
        await Customer.RemoveAll(RemoveStrategy.Safe, ct);
    }
}

// ✅ Fast: Known scenario where hooks aren't needed
public async Task PurgeArchivedData()
{
    // Explicitly bypass hooks for maximum performance
    await ArchivedOrder.RemoveAll(RemoveStrategy.Fast, ct);
    await ArchivedLog.RemoveAll(RemoveStrategy.Fast, ct);
}

// ✅ Optimized: Temporary data cleanup
public async Task CleanupTempData()
{
    // Framework chooses optimal strategy based on provider
    await TempFile.RemoveAll(ct);
}
```

**Important Warnings**

⚠️ **CRITICAL: Optimized Strategy Bypasses Hooks on Most Providers**

```csharp
// Default RemoveAll() uses Optimized strategy
// On Postgres/SQL Server/MongoDB/SQLite/Redis: Optimized = Fast (bypasses hooks!)
// On JSON/InMemory/Couchbase: Optimized = Safe (fires hooks)

public static class OrderLifecycle
{
    public static void Configure(EntityLifecycleBuilder<Order> builder) =>
        builder.BeforeDelete(async (ctx, next) =>
        {
            await AuditLog.RecordDeletion(ctx.Entity);
            await next();
        });
}

// Optimized (default): Hooks BYPASSED on Postgres/SQL Server/Mongo (uses TRUNCATE)
await Order.RemoveAll(ct); // ✗ Hooks skipped on most providers!

// Explicit Safe: Hooks ALWAYS fire regardless of provider
await Order.RemoveAll(RemoveStrategy.Safe, ct); // ✓ Hooks fire, audit recorded

// Explicit Fast: Hooks ALWAYS bypassed regardless of provider
await Order.RemoveAll(RemoveStrategy.Fast, ct); // ✗ Hooks skipped for performance
```

**When Audit Trail Required:** Always use `RemoveStrategy.Safe` to guarantee hooks fire:

```csharp
// Production tenant deletion - require audit trail
public async Task DeleteTenant(string tenantId)
{
    using (EntityContext.Partition($"tenant-{tenantId}"))
    {
        // Explicit Safe ensures audit hooks fire on ALL providers
        await Order.RemoveAll(RemoveStrategy.Safe, ct);
        await Customer.RemoveAll(RemoveStrategy.Safe, ct);
    }
}
```

⚠️ **Return Value May Be -1 (Unknown Count)**

```csharp
// TRUNCATE doesn't report deleted count in some providers
var count = await Todo.RemoveAll(RemoveStrategy.Fast, ct);
if (count == -1)
{
    // Postgres/SQL Server TRUNCATE: count unavailable
    Logger.LogInformation("All todos removed (count unknown)");
}
else
{
    Logger.LogInformation($"Removed {count} todos");
}
```

⚠️ **Permission Requirements Differ**

```csharp
// TRUNCATE requires ALTER permission (vs DELETE permission for Safe)
// If user lacks ALTER, TRUNCATE fails → auto-fallback to DELETE

try
{
    await Todo.RemoveAll(RemoveStrategy.Fast, ct);
}
catch (Exception ex) when (ex.Message.Contains("permission"))
{
    // User may have DELETE but not ALTER permission
    Logger.LogWarning("TRUNCATE failed, using Safe strategy");
    await Todo.RemoveAll(RemoveStrategy.Safe, ct);
}
```

**Usage Scenarios**

Default `RemoveAll()` uses Optimized strategy: TRUNCATE/DROP on capable providers (Postgres, SQL Server, MongoDB, SQLite, Redis) for 10-250x performance, safe DELETE on others (JSON, InMemory, Couchbase). **Critical**: Optimized bypasses lifecycle hooks on most providers. Use explicit `RemoveStrategy.Safe` when audit trails are required. Tests benefit from automatic fast cleanup (200ms vs 45s for 1M records). Framework handles provider differences transparently.

---

## 4. Context Routing: Partitions, Sources, Adapters

**Concepts**

**Partition:** logical suffix (`Todo#archive`) for per-cohort isolation. **Source:** named configuration (`Koan:Data:Sources:{name}`) that picks adapter and connection string. **Adapter:** explicit provider override (`EntityContext.Adapter("mongo")`). **Rule:** Source XOR Adapter (ADR DATA-0077). Each scope is ambient (AsyncLocal) and replaced by nested scopes.

**Recipe**

Reference adapter packages for each provider you want to target (e.g., `Koan.Data.Connector.Postgres`, `Koan.Data.Connector.Mongo`). Configure `Koan:Data:Sources` accordingly.

**Sample**

```csharp
using (EntityContext.Partition("archive"))
{
    await new Todo { Title = "Archived" }.Save();
}

using (EntityContext.Source("analytics"))
{
    var completed = await Todo.Count(t => t.Completed);
}

using (EntityContext.Adapter("mongo"))
{
    var mongoTodos = await Todo.All();
}
```

**Usage Scenarios**

Multi-tenant applications isolate data via partitions (`Entity#tenant-alpha`). Analytics queries route to dedicated read-replica sources without touching transactional stores.

**Scope Nesting**

```csharp
using (EntityContext.Source("archive"))
using (EntityContext.Partition("cold"))
{
    await Todo.Copy().To(partition: "cold-snapshot").Run();
}
```

Boot the runtime with `builder.Services.AddKoan();` in `Program.cs`. Everything below builds on this foundation.

---

## 5. Transaction Coordination

**Concepts**

Ambient transaction support coordinates entity operations across multiple adapters with best-effort atomicity. Operations are tracked in memory and executed on commit or rollback. Transactions auto-commit on dispose (minimal cognitive load) or can be explicitly committed/rolled back. Named transactions provide correlation for telemetry and debugging.

**Recipe**

Add transaction support to DI in `Program.cs`:

```csharp
builder.Services.AddKoan();
builder.Services.AddKoanTransactions(options =>
{
    options.AutoCommitOnDispose = true;  // Default: auto-commit
    options.EnableTelemetry = true;      // Activity spans + logging
    options.MaxTrackedOperations = 10_000;
});
```

**Simple Transaction (Auto-Commit)**

```csharp
// Auto-commit on dispose (recommended for simple scenarios)
using (EntityContext.Transaction("save-project"))
{
    var project = new Project { Name = "My Project" };
    await project.Save(ct);

    var job = new Job { ProjectId = project.Id, Name = "Job 1" };
    await job.Save(ct);

    // Auto-commit when using block exits
}
```

**Explicit Commit/Rollback**

```csharp
// Explicit commit for critical operations
using (EntityContext.Transaction("batch-import"))
{
    foreach (var item in items)
    {
        await item.Save(ct);
    }

    if (validationFailed)
    {
        await EntityContext.RollbackAsync(ct);
        return;
    }

    await EntityContext.CommitAsync(ct);
}
```

**Cross-Adapter Coordination**

```csharp
// Coordinate operations across SQLite and SQL Server
using (EntityContext.Transaction("cross-adapter"))
{
    // Save to default adapter (SQLite)
    await cacheEntry.Save(ct);

    // Save to SQL Server
    using (EntityContext.Adapter("sqlserver"))
    {
        await userRecord.Save(ct);
    }

    // Best-effort commit both
    await EntityContext.CommitAsync(ct);
}
```

**Transaction Context**

```csharp
// Check if in transaction
if (EntityContext.InTransaction)
{
    var capabilities = EntityContext.Capabilities;
    logger.LogInformation("Tracking {Count} operations across {Adapters} adapter(s)",
        capabilities.TrackedOperationCount,
        capabilities.Adapters.Length);
}
```

**Usage Scenarios**

Applications coordinate multi-step entity creation (project + jobs) with atomic all-or-nothing behavior. Data migrations use transactions with rollback on failure to ensure consistency. Cross-adapter operations (e.g., primary + backup storage) coordinate saves with best-effort atomicity across SQLite, SQL Server, JSON, or other providers.

**Performance Considerations**

Transactions track operations in memory. For large batches, break into smaller transactions (1,000 operations each) to avoid excessive memory usage. Use `MaxTrackedOperations` configuration to prevent unbounded growth.

**Important Notes**

- **Best-Effort Atomicity**: Framework provides sequential execution with error reporting, not true distributed transactions
- **Nested Transactions**: Not supported - throws `InvalidOperationException`
- **Infrastructure Operations**: `RemoveAll()` and `Truncate()` bypass transactions
- **Deferred Execution**: Entity saves/deletes are tracked, not executed immediately

For detailed usage patterns, examples, and troubleshooting, see the [Transaction Support Usage Guide](transactions-usage-guide.md).

---

## 6. Advanced Transfers: Copy, Move, Mirror

**Concepts**

`Copy()` clones entities into another context. `Move()` clones then deletes from origin (strategies: `AfterCopy`, `Batched`, `Synced`). `Mirror()` synchronizes data in one or both directions; `[Timestamp]` resolves conflicts. `.Audit()` receives per-batch telemetry; `TransferResult<TKey>` summarizes counts, warnings, conflicts.

**Recipe**

Latest `Koan.Data.Core` (transfer builders live in `Koan.Data.Core.Transfers`). `System.ComponentModel.DataAnnotations` when using `[Timestamp]`. Adapters for origin and destination contexts. Optional logging for `.Audit`.

**Samples**

```csharp
await Todo.Copy(t => !t.Completed)
         .To(partition: "inactive")
         .Audit(batch => logger.LogInformation("Copied {Count}", batch.BatchCount))
         .Run();

await Todo.Move()
         .WithDeleteStrategy(DeleteStrategy.Synced)
         .From(partition: "hot")
         .To(adapter: "postgres", partition: "warm")
         .Run();

await Todo.Mirror(mode: MirrorMode.Bidirectional)
         .To(source: "reporting")
         .Run();
```

**Usage Scenarios**

Testing frameworks use `Copy()` to preload test data and `Mirror()` to sync results without hand-written loops. Ops teams move cold data into cheaper storage overnight with `Move()` and a specific delete strategy. Bidirectional `Mirror()` keeps reporting and transactional stores aligned while surfacing conflicts when timestamps are missing.

**Query-Shaped Transfer**

```csharp
await Todo.Copy(query => query.Where(t => t.Tags.Contains("ops")))
         .To(source: "analytics")
         .Run();
```

**Inspecting Results**

```csharp
var result = await Todo.Move().To(partition: "cold").Run();
logger.LogInformation("Copied {Copied}/Deleted {Deleted}", result.CopiedCount, result.DeletedCount);
result.Audit.Last().IsSummary.Should().BeTrue();
```

---

## 7. Streaming Workloads, Flow and Jobs

**Concepts**

Use `AllStream`/`QueryStream` in Koan Flow for large data pipelines. Long-running or scheduled transfers belong in Koan Jobs for checkpointing and retries.

**Recipe**

Add `Koan.Flow` package for pipeline DSL. Add `Koan.Jobs.Core` when you need resumable or scheduled execution. Ensure adapters support efficient paging for streaming.

**Flow Pipeline (S5)**

```csharp
await Flow.Pipeline("embedding-backfill")
          .ForEach(await Recommendation.AllStream(batchSize: 200))
          .Do(async (rec, ct) =>
          {
              rec.Embedding = await EmbedAsync(rec.Content, ct);
              await rec.Save();
          })
          .RunAsync(ct);
```

**Job for Nightly Archive**

```csharp
public class ArchiveJob : IJob
{
    public async Task ExecuteAsync(JobContext ctx)
    {
        await Todo.Move()
                 .From(partition: "hot")
                 .To(partition: "cold")
                 .Run(ctx.CancellationToken);
    }
}
```

**Usage Scenarios**

Applications stream entity updates to Flow pipelines, generating embeddings at scale without full materialization. Jobs provide reliable scheduling and resumption for nightly transfers or DR syncs.

---

## 8. AI and Vector Extensions

**Concepts**

Store embeddings directly on entities, integrate with vector providers, export caches (ADR-0051). Works with the same `Entity<T>` patterns.

**Recipe**

Reference `Koan.Data.Vector.Abstractions` and the specific connector (e.g., `Koan.Data.Vector.Connector.Weaviate`). Configure vector source(s) in settings.

**Sample**

```csharp
public class Recommendation : Entity<Recommendation>
{
    public float[]? Embedding { get; set; }
}

var matches = await Recommendation.Query("vectorDistance < 0.15");
```

**Usage Scenarios**

Applications export vector embeddings into a cache via `Copy()` so APIs can respond instantly. Architects can swap vector backends (Weaviate, Pinecone, etc.) by changing configuration, no application code changes.

---

## 9. Observability and Testing

**Concepts**

`TransferResult<TKey>` surfaces counts, warnings, conflicts, audit batches. BootReport adds module notes for introspection. `Koan.Testing` simplifies verifying context routing and partitions.

**Recipe**

Logging via `Microsoft.Extensions.Logging`. Optional: BootReport consumers for module diagnostics. Add `Koan.Testing` to test projects.

**Sample**

```csharp
var result = await Todo.Copy().To(partition: "snapshot").Run();
result.Audit.Last().IsSummary.Should().BeTrue();
result.Warnings.Should().BeEmpty();

var conflicts = await Todo.Mirror(mode: MirrorMode.Bidirectional)
                           .To(source: "reporting")
                           .Run();
if (conflicts.HasConflicts)
{
    logger.LogWarning("{Count} conflicts detected", conflicts.Conflicts.Count);
}
```

**Usage Scenarios**

QA teams examine audit summaries to ensure Move operations deleted exactly what was copied. Architects rely on BootReport output to confirm sources and partitions are configured as expected during boot.

---

## 10. Deployment Readiness

**Concepts**

Seeding and cleanup using `RemoveAll` per partition/source. Schema guard (`EntitySchemaGuard`) for health checks. Koan Jobs for durable, resumable transfers.

**Recipe**

Same packages; add jobs and health check packages if desired. Configure health checks in host builder.

**Sample**

```csharp
await Todo.RemoveAll();
await new List<Todo>
{
    new() { Title = "Seed 1" },
    new() { Title = "Seed 2" }
}.Save();

services.AddHealthChecks()
        .AddCheck<EntitySchemaHealthCheck<Todo, string>>("todo-schema");
```

**Usage Scenarios**

Teams wipe and reseed tenants during staging deploys without hand-written scripts. Health checks detect missing indexes or migrations before traffic hits the service. Long-running archive jobs leverage Koan Jobs for retry and progress tracking.

---

## Next Steps

1. Explore the referenced samples (`samples/guides/g1c1.GardenCoop`, `samples/S5.Recs`, `samples/S14.AdapterBench`) to see these concepts in action.
2. Extend the transfer DSL in your domain—add `.Mirror()` runs before cut-overs, or `Copy()` recipes to hydrate analytics sources.
3. Combine Flow + Jobs with the transfer DSL to orchestrate large data migrations safely.

When in doubt, stick to the entity-first patterns above. They keep your code declarative, provider-agnostic, and ready for Koan’s automation pillars.
