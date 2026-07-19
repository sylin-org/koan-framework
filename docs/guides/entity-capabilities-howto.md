---
type: GUIDE
domain: data
title: "Entity Capabilities How-To"
audience: [developers, architects]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  status: not-yet-tested
  scope: docs/guides/entity-capabilities-howto.md
related_guides:
  - ai-vector-howto.md
  - canon-capabilities-howto.md
  - patch-capabilities-howto.md
---

# Koan Entity Capabilities: Your Complete Guide

This guide walks you through Koan's data layer, from your first `todo.Save()` to production-scale multi-provider applications with streaming, transactions, and batch operations. Think of it as a conversation with a colleague who's been down this path before—we'll start simple and build your confidence as we go.

Each section follows a gentle rhythm: **Concepts** (what is this?), **Recipe** (how do I set it up?), **Sample** (show me the code), and **Usage Scenarios** (when would I use this?). By the end, you'll know how to choose the right patterns for your needs.

**Related Guides:**
- Need semantic search? → [AI & Vector How-To](ai-vector-howto.md)
- Multi-source data deduplication? → [Canon Capabilities](canon-capabilities-howto.md)
- Partial entity updates? → [Patch Capabilities](patch-capabilities-howto.md)

---

## 0. Prerequisites

Before we dive in, let's get your environment ready. Don't worry—Koan is designed to work with sensible defaults, so you can skip configuration and come back to it later.

**Add the Koan baseline packages:**

```xml
<PackageReference Include="Koan.Core" Version="0.6.3" />
<PackageReference Include="Koan.Data.Core" Version="0.6.3" />
<PackageReference Include="Koan.Data.Abstractions" Version="0.6.3" />
```

**Pick at least one data adapter** (SQLite is great for getting started):

```xml
<PackageReference Include="Koan.Data.Connector.Sqlite" Version="0.6.3" />
```

**Optional:** Configure your default data source in `appsettings.json`. Koan works without this—it'll use in-memory storage—but when you're ready for persistence:

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

**Boot the runtime** in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();  // That's it—Koan handles the rest
var app = builder.Build();
app.Run();
```

Everything below builds on this foundation. Let's go!

---

## 1. Foundations: Your First Entity

**Concepts**

Think of `Entity<T>` as your data model with superpowers. It gives you:
- Auto-generated GUID v7 IDs (time-ordered, no collisions)
- Instance methods for lifecycle (`Save`, `Remove`)
- Static helpers for querying (`Get`, `All`, `Query`, `Count`)
- Provider-agnostic persistence (works with SQLite, Postgres, MongoDB, and more)

Everything routes through your configured data source—no manual repository plumbing.

**Recipe**

You've already got the packages from Prerequisites. No additional setup needed!

**Sample**

Let's create a simple todo entity and save it:

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
    public DateTimeOffset? DueDate { get; set; }
}

// Create and save a todo
var todo = new Todo
{
    Title = "Learn Koan Entity patterns",
    DueDate = DateTimeOffset.UtcNow.AddDays(7)
};
await todo.Save();  // That's it! Auto-persisted to your configured adapter

// Fetch it back
var fetched = await Todo.Get(todo.Id);
Console.WriteLine($"Found: {fetched?.Title}");

// Update it
fetched!.Completed = true;
await fetched.Save();  // Same method for create and update

// Remove it when done
await fetched.Remove();
```

**Why this works:** Koan tracks whether an entity is new or existing, automatically choosing insert vs update behind the scenes.

**Custom Keys**

Most of the time, GUID v7 IDs are perfect—they're time-ordered and distributed-safe. But if you need custom keys (like auto-increment integers or composite keys):

```csharp
public class Product : Entity<Product, int>
{
    public override int Id { get; set; }  // You control the ID
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// You assign the ID explicitly
var product = new Product
{
    Id = 42,
    Name = "Widget",
    Price = 19.99m
};
await product.Save();
```

**Sharing Shape Across Entities**

Sooner or later you'll have several entities that share fields: a family of catalog records, a set of job types, audited rows that all carry `CreatedBy`. The instinct from classic OOP is to inherit one entity from another. Don't.

```csharp
// Anti-pattern: do NOT inherit one concrete entity from another
public class Model  : Entity<Model> { public string? Shared { get; set; } }
public class Model2 : Model { public string? Extra { get; set; } }  // compiles, looks fine, isn't
```

This compiles and reads naturally, but it silently splits writes from reads:

- `Save` is `Save<TEntity>(this TEntity)`, so `new Model2().Save()` infers `TEntity = Model2` and writes to a dedicated **Model2** collection (full shape, `Extra` included).
- `Get` is an inherited static fixed at the CRTP root `Entity<Model>`, so both `Model.Get(id)` AND `Model2.Get(id)` read the **Model** collection.

Net result: the `Model2` row persists correctly but is unreadable through either `.Get(id)`. No exception, no warning. (Validated against real MongoDB.) C# does not let an inherited static re-specialize for the deriving type, so `Model2.Get` simply *is* `Model.Get`.

**The fix: make each entity its own `Entity<T>` root, and lift shared shape into a generic base.**

```csharp
// Shared fields via a generic base; each entity is its own set
public abstract class CatalogEntity<T> : Entity<T> where T : CatalogEntity<T>, new()
{
    public string? Shared { get; set; }   // every catalog entity gets this
}

public class Package : CatalogEntity<Package> { }
public class Mirror  : CatalogEntity<Mirror>  { public string? Extra { get; set; } }

await new Package { Shared = "x" }.Save();
var p = await Package.Get(id);   // bare .Get(id), returns Package, own collection
var m = await Mirror.Get(id);    // bare .Get(id), returns Mirror, own collection, isolated
```

`Package` and `Mirror` are now **siblings** that share shape, each with its own collection. Same-id rows in different types never collide. The bare `Foo.Get(id)` ergonomic is preserved with zero ceremony: no source generator, no `partial`, no `Entity<T>.Get(...)` call form.

**What you trade:** siblings are not substitutable. `Mirror` is not a `Package`, so you cannot pass one where the other is expected or query both as one set. That is the only thing class inheritance would have given you, and it is rarely what you actually want for stored entities. If you genuinely need a polymorphic set (query a base type and get mixed concrete rows back), that is single-table inheritance: one shared collection plus a type discriminator, an explicit opt-in, not the default. For the common case (share fields, store separately), sibling CRTP roots are the answer.

**Rule of thumb:** every concrete entity should read `class Foo : Entity<Foo>` or `class Foo : SomeBase<Foo>`. If you ever see `class Foo : SomeConcreteEntity` (inheriting a type that is itself a set), stop: that is the footgun.

Entity property names must also be unique without relying on case. A model with inherited `Id` and a
second property named `id`, for example, has no portable identity across JSON and providers with
different case-sensitivity rules. Koan rejects that model on first use, before opening a repository,
and names the properties to rename. Normal PascalCase properties still accept unambiguous camelCase or
mixed-case filter input.

**Usage Scenarios**

- **Web APIs:** Entity<T> eliminates boilerplate. No manual repositories, no hand-written CRUD.
- **Background jobs:** Save entities directly in job handlers—Koan manages connections and transactions.
- **Multi-provider apps:** Same `todo.Save()` code works whether you're using SQLite locally or Postgres in production.

---

## 2. Batch Retrieval: Avoiding the N+1 Trap

**Concepts**

Here's a common mistake: loading related entities one-by-one in a loop. It's called the N+1 problem, and it kills performance. Koan's batch retrieval fetches multiple entities in a single query—preserving order and handling missing IDs gracefully.

**When you need this:** Loading playlist items, fetching related records, validating a list of IDs.

**Recipe**

No new packages needed. Batch retrieval is built into `Entity<T>`.

**Sample**

```csharp
// ❌ The N+1 anti-pattern (don't do this!)
var todos = new List<Todo?>();
foreach (var id in collectionIds)  // 100 IDs = 100 database queries!
{
    todos.Add(await Todo.Get(id, ct));
}

// ✅ Efficient batch retrieval (1 database query!)
var ids = new[] { id1, id2, id3, id4, id5 };
var todos = await Todo.Get(ids, ct);
// Result: [Todo?, Todo?, null, Todo?, Todo?]
// Third ID wasn't found—Koan returns null in that position

// Filter out missing items if needed
var foundTodos = todos.Where(t => t != null).Select(t => t!).ToList();
```

**Order matters:** Batch Get returns results in the same order as your input IDs. This is perfect for paginated collections where order is meaningful.

**Performance Benefits**

```
Single Get: 1 query, ~2ms
Batch Get (100 IDs): 1 query with IN clause, ~5ms
Loop of 100 Gets: 100 queries, ~200ms+

Speedup: 40x faster!
```

**Real-World Example: Paginated Playlist**

```csharp
// Load a playlist page
var playlist = await Playlist.Get(playlistId, ct);
var pageIds = playlist.SongIds
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToList();

// Single batch query for the entire page
var songs = await Song.Get(pageIds, ct);

// Order is preserved—songs appear in playlist order
return songs.Where(s => s != null).Select(s => s!).ToList();
```

**Partition-aware batches:**

```csharp
// Load archived todos from a specific partition
using (EntityContext.Partition("archive"))
{
    var archivedTodos = await Todo.Get(todoIds, ct);
}
```

**Usage Scenarios**

- **Collection/playlist pagination:** Fetch a page of pre-ordered items efficiently
- **Relationship navigation:** Load all related entities without N+1 queries
- **Bulk validation:** Check which IDs exist before processing
- **API responses:** Hydrate response DTOs with minimal database round-trips

**Pro tip:** If you're fetching items in a loop, ask yourself: "Could I batch this?" The answer is usually yes.

---

## 3. Querying and Pagination

**Concepts**

Koan gives you three ways to query:
1. **LINQ expressions** (`Query(t => t.Completed)`)—type-safe, IntelliSense-friendly
2. **String queries** (`Query("status == 'active'")`)—useful for dynamic filters
3. **Pagination helpers** (`FirstPage()`, `Page(2, 20)`)—built-in page support

Providers that support server-side LINQ (Postgres, MongoDB, SQL Server) execute filters in the database. Others (JSON, InMemory) fall back to client-side evaluation—Koan warns you when this happens.

**Recipe**

Same packages as before. For best performance, use adapters with `IQueryRepository` support (Postgres, MongoDB, SQL Server).

**Sample**

**Basic LINQ queries:**

```csharp
// Find overdue todos
var overdue = await Todo.Query(t =>
    !t.Completed &&
    t.DueDate.HasValue &&
    t.DueDate.Value < DateTimeOffset.UtcNow,
    ct
);

// String-based query (useful for user-built filters)
var readings = await SensorReading.Query("temperature > 25 AND location == 'warehouse'", ct);
```

**Pagination:**

```csharp
// Get the first page (page 1, 20 items)
var firstPage = await Todo.FirstPage(pageSize: 20, ct);

// Get a specific page
var page3 = await Todo.Page(page: 3, size: 20, ct);

// Combine with filtering
var completed = await Todo.Query(
    t => t.Completed,
    new QueryDefinition { Page = 2, PageSize = 50 },
    ct
);
```

**Pagination with total counts (for UI controls):**

```csharp
var result = await Todo.QueryWithCount(
    t => t.ProjectId == projectId,
    new QueryDefinition { Page = 1, PageSize = 20 }.WithSort<Todo>("-Created"), // "-" prefix = descending
    ct
);

Console.WriteLine($"Showing {result.Items.Count} of {result.TotalCount} todos");
// Output: Showing 20 of 347 todos
```

**Streaming large datasets:**

When a result is too large to materialize as one Entity list, use provider-bounded streaming on a
qualified adapter:

```csharp
// Consumer-paced provider pages on a qualified adapter
await foreach (var reading in SensorReading.QueryStream(
    reading => reading.Timestamp > cutoff,
    batchSize: 500,  // At most 500 Koan-visible candidates per provider page
    ct: ct))
{
    await ProcessReading(reading, ct);
}
```

This requires `ProviderBoundedPaging` (SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, or
Couchbase). InMemory, JSON, and Redis reject before query/yield. The batch bounds Koan-visible
candidates; it does not promise snapshot consistency, mutation safety, or opaque-driver memory.

**Usage Scenarios**

- **Web APIs:** Paginate results for list endpoints (`GET /api/todos?page=2&size=20`)
- **Reports:** On a qualified adapter, process large datasets without materializing the complete Entity source
- **Dashboards:** Query with counts for "Showing X of Y" UI controls
- **Admin tools:** Use string queries for user-built dynamic filters

**Pro tip:** If you're rendering a grid or list, you almost always want `QueryWithCount()` for pagination controls.

---

## 4. Counting Strategies: Fast vs Exact

**Concepts**

How many todos do you have? Koan gives you three counting strategies so you can state the accuracy
you need:

- **Exact:** Request an exact result
- **Fast:** Request an adapter-specific fast path; the result may be an estimate
- **Optimized:** Let the adapter choose its optimized count path

Think of it like this: counting jelly beans in a jar—you can count each one (Exact) or estimate from the jar's size (Fast).

**When to use each:**
- Dashboard summaries? **Fast** can fit when an estimate is acceptable and the selected adapter supports it
- Business-critical inventory check? **Exact** is essential
- General use? **Optimized** is the default; verify the selected adapter's behavior for your workload

**Recipe**

No new packages. Count strategies are built into Entity<T>.

**Sample**

**Simple counts:**

```csharp
// Default: pass CountStrategy.Optimized to the selected adapter
var total = await Todo.Count;

// Explicit exact count (guaranteed accuracy)
var exact = await Todo.Count.Exact(ct);

// Explicit adapter-specific fast path; the result may be an estimate
var fast = await Todo.Count.Fast(ct);

// Filtered count
var completed = await Todo.Count.Where(t => t.Completed, ct: ct);

// Filtered with explicit strategy
var urgent = await Todo.Count.Where(
    t => t.Priority > 3,
    CountStrategy.Fast,
    ct: ct
);

// Count a specific partition
var archivedCount = await Todo.Count.Partition("archive", ct: ct);
```

Count cost, accuracy, and optimization are adapter-specific. Benchmark the elected adapter and
dataset rather than relying on cross-provider timings.

**When to use each strategy:**

```csharp
// ✅ Pagination UI (fast is fine)
var totalPages = (await Todo.Count.Fast(ct) + pageSize - 1) / pageSize;

// ✅ Dashboard summary (estimates OK)
var stats = new
{
    Total = await Todo.Count.Fast(ct),
    Completed = await Todo.Count.Where(t => t.Completed, CountStrategy.Fast, ct: ct),
    Pending = await Todo.Count.Where(t => !t.Completed, CountStrategy.Fast, ct: ct)
};

// ✅ Business logic (exact required!)
var exactInventory = await Product.Count.Where(
    p => p.InStock && p.WarehouseId == warehouseId,
    CountStrategy.Exact,
    ct: ct
);
if (exactInventory < reorderThreshold)
    await TriggerRestock(ct);
```

**Usage Scenarios**

- **Dashboards:** Use Fast for real-time stats that refresh every few seconds
- **Pagination:** Fast counts power "page 1 of 42" without blocking
- **Critical operations:** Exact counts for financial reconciliation, inventory
- **Default case:** Use Optimized—let Koan choose Fast when safe, Exact when necessary

**Pro tip:** If you're showing approximate totals to users ("~1.2M results"), Fast is perfect. Save Exact for when accuracy truly matters.

---

## 5. Batch Operations and Lifecycle Hooks

**Concepts**

You've learned to save single entities. But what about bulk imports? Product catalogs? Historical migrations? That's where batch operations shine.

Koan also gives you **lifecycle hooks**—think of them as middleware for your entities. Want to validate titles before save? Auto-format fields after load? Prevent accidental ID changes? Hooks handle it declaratively.

**Recipe**

Dependencies already covered. Lifecycle is part of `Koan.Data.Core`.

**Sample**

**Bulk saves:**

```csharp
// Seed 1,000 todos in one batch
var todos = Enumerable.Range(1, 1000)
    .Select(i => new Todo
    {
        Title = $"Task {i}",
        Completed = i % 5 == 0  // Every 5th is completed
    })
    .ToList();

await todos.Save(ct);  // Single bulk insert—much faster than 1000 individual saves
```

**Batch operations (add/update/delete):**

```csharp
// Combine multiple operations in one transaction
await Todo.Batch()
    .Add(new Todo { Title = "New task" })
    .Update(existingId, todo => todo.Completed = true)
    .Delete(oldId)
    .SaveAsync(ct);
```

**Lifecycle policy:**

```csharp
builder.Services.AddKoan(() =>
    Todo.Lifecycle
        .BeforeUpsert(ctx =>
    {
        ctx.Protect(nameof(Todo.Id));
        if (string.IsNullOrWhiteSpace(ctx.Current.Title))
            return ctx.Cancel("Todo must have a title");
        if (ctx.Current.Title.Length > 200)
            return ctx.Cancel("Title too long (max 200 chars)");
        return ctx.Proceed();
    })
        .AfterLoad(ctx =>
        {
            if (ctx.Current.Title != null)
                ctx.Current.Title = ctx.Current.Title.Trim();
        }));
```

**How hooks work:**
1. `AddKoan(...)` owns the plan for this host; no static reset or registrar is required.
2. `ctx.Protect(name)` prevents later handlers from mutating the captured value.
3. `BeforeUpsert` validates before save; `ctx.Cancel(reason)` rejects before persistence.
4. `AfterLoad` formats the materialized application instance without persisting it.

**Usage Scenarios**

- **Bulk imports:** Process CSV files, API responses, or migrations in batches
- **Data validation:** Enforce business rules before persistence
- **Audit trails:** Log changes in `BeforeUpdate` hooks
- **Display formatting:** Auto-trim, capitalize, or format fields after load
- **Protection:** Prevent accidental ID or audit field changes

**Pro tip:** Lifecycle hooks keep business logic close to your data model. No need to remember validation in every controller—define it once in the entity lifecycle.

---

## 6. Bulk Removal: Choosing Your Strategy

**Concepts**

Eventually, you'll need to delete a lot of data—test cleanup, archival, tenant deletion. Koan gives you three strategies:

- **Safe:** Always fires lifecycle hooks (audit trails, cleanup logic)
- **Fast:** Bypasses hooks for 10-250x performance (TRUNCATE, DROP, etc.)
- **Optimized:** Preserves configured remove Lifecycle; otherwise delegates optimization to the provider

Here's the key insight: configured remove Lifecycle makes `Optimized` use the safe per-entity path.
Explicit `Fast` is the only intentional lifecycle bypass.

**Quick decision:**
- Need audit trail or cleanup logic? → **Safe**
- Test cleanup or temp data? → **Optimized** (default)
- Know hooks aren't needed? → **Fast** (explicit)

**Recipe**

Same packages as always. Providers with `DataCaps.Write.FastRemove` (Postgres, SQL Server, MongoDB, SQLite, Redis) support fast strategies.

**Sample**

**Basic removal:**

```csharp
// Default: Optimized (preserves configured remove Lifecycle)
var count = await Todo.RemoveAll(ct);

// Explicit Safe (always fires hooks)
var count = await Todo.RemoveAll(RemoveStrategy.Safe, ct);

// Explicit Fast (maximum performance, no hooks)
var count = await Todo.RemoveAll(RemoveStrategy.Fast, ct);

// Remove from specific partition
var archived = await Todo.RemoveAll(RemoveStrategy.Optimized, "archive", ct);

// Scoped removal via EntityContext
using (EntityContext.Partition("temp-data"))
{
    await Todo.RemoveAll(ct);  // Removes only from temp-data partition
}
```

**Performance comparison (1 million rows):**

| Provider   | Safe       | Fast/Optimized | Implementation              | Speedup |
|------------|------------|----------------|-----------------------------|---------|
| PostgreSQL | ~45 sec    | ~200ms         | `TRUNCATE TABLE`            | 225x    |
| SQL Server | ~38 sec    | ~150ms         | `TRUNCATE TABLE`            | 253x    |
| MongoDB    | ~52 sec    | ~300ms         | Drop + recreate indexes     | 173x    |
| SQLite     | ~25 sec    | ~2 sec         | `DELETE` + `VACUUM`         | 12.5x   |
| Redis      | ~18 sec    | ~800ms         | `UNLINK` (async)            | 22.5x   |

**When to use each:**

```csharp
// ✅ Test cleanup (Optimized is perfect)
[Fact]
public async Task BulkImportTest()
{
    await Todo.RemoveAll(ct);  // Fast cleanup between tests
    var todos = GenerateTestData(1000);
    await todos.Save(ct);
    // ... assertions
}

// ✅ Production tenant deletion (Safe required for audit!)
public async Task DeleteTenant(string tenantId, CancellationToken ct)
{
    using (EntityContext.Partition($"tenant-{tenantId}"))
    {
        // Explicit Safe ensures audit hooks fire
        await Order.RemoveAll(RemoveStrategy.Safe, ct);
        await Customer.RemoveAll(RemoveStrategy.Safe, ct);
        await Invoice.RemoveAll(RemoveStrategy.Safe, ct);
    }
}

// ✅ Known no-audit scenario (Fast for max speed)
public async Task PurgeArchivedLogs(CancellationToken ct)
{
    using (EntityContext.Partition("archive-2023"))
    {
        await Log.RemoveAll(RemoveStrategy.Fast, ct);  // No audit needed
    }
}
```

**Important:** `Fast` explicitly bypasses remove Lifecycle. `Optimized` and `Safe` preserve it when
handlers are configured:

```csharp
builder.Services.AddKoan(() =>
    Order.Lifecycle.BeforeRemove(async ctx =>
        {
            // Log deletion for compliance
            await AuditLog.RecordDeletion(ctx.Current.Id, ctx.Current.CustomerId);
            return ctx.Proceed();
        }));

// ✅ Optimized detects the configured remove Lifecycle and preserves it
await Order.RemoveAll(ct);

// ✅ Safe also guarantees per-entity Lifecycle
await Order.RemoveAll(RemoveStrategy.Safe, ct);

// ⚠️ Fast is an explicit lifecycle bypass
await Order.RemoveAll(RemoveStrategy.Fast, ct);
```

**Usage Scenarios**

- **Test cleanup:** Optimized gives fast resets between tests
- **Production deletion:** Safe ensures audit trails for compliance
- **Archive purging:** Fast for known no-audit scenarios
- **Staging resets:** Optimized for quick environment resets

**Pro tip:** Check `Entity.SupportsFastRemove` to see if your provider supports fast strategies. In-memory and JSON adapters always use Safe (they're already fast).

---

## 7. Context Routing: Partitions, Sources, and Adapters

**Concepts**

So far, everything's been saved to your default data source. But real applications need more:
- **Multi-tenancy:** Isolate tenant data (`tenant-alpha`, `tenant-beta`)
- **Read replicas:** Query analytics sources without touching production
- **Provider mixing:** Cache in Redis, store in Postgres, archive in S3

Koan gives you three routing primitives:
- **Partition:** Logical suffix for multi-tenant isolation (`Todo#tenant-alpha`)
- **Source:** Named configuration pointing to a specific adapter+connection
- **Adapter:** Explicit provider override for one-off scenarios

**Rule:** Use Source XOR Adapter (never both). Partitions work with either.

**Recipe**

Reference adapter packages for each provider you want:
```xml
<PackageReference Include="Koan.Data.Connector.Postgres" Version="0.6.3" />
<PackageReference Include="Koan.Data.Connector.Mongo" Version="0.6.3" />
<PackageReference Include="Koan.Data.Connector.Redis" Version="0.6.3" />
```

Configure sources in `appsettings.json`:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=app.db"
        },
        "Analytics": {
          "Adapter": "postgres",
          "ConnectionString": "Host=analytics-db;Database=reporting"
        },
        "Cache": {
          "Adapter": "redis",
          "ConnectionString": "localhost:6379"
        }
      }
    }
  }
}
```

**Sample**

**Partitions (multi-tenancy):**

```csharp
// Save to tenant-specific partition
using (EntityContext.Partition("tenant-alpha"))
{
    await new Todo { Title = "Alpha's todo" }.Save(ct);
}

using (EntityContext.Partition("tenant-beta"))
{
    await new Todo { Title = "Beta's todo" }.Save(ct);
}

// Query tenant data
using (EntityContext.Partition("tenant-alpha"))
{
    var alphaTodos = await Todo.All(ct);  // Only sees alpha's data
}
```

**Sources (read replicas, analytics):**

```csharp
// Write to production
await new Todo { Title = "Production todo" }.Save(ct);

// Read from analytics replica
using (EntityContext.Source("Analytics"))
{
    var stats = new
    {
        Total = await Todo.Count.Fast(ct),
        Completed = await Todo.Count.Where(t => t.Completed, ct: ct)
    };
}

// Cache frequently accessed data
using (EntityContext.Source("Cache"))
{
    var popularProducts = await Product.Query(
        p => p.ViewCount > 10000,
        ct
    );
}
```

**Adapters (one-off provider overrides):**

```csharp
// Usually you'd use Sources, but adapters work for quick tests
using (EntityContext.Adapter("mongo"))
{
    var mongoTodos = await Todo.All(ct);  // Fetches from MongoDB
}
```

**Nesting contexts:**

```csharp
// Combine routing primitives
using (EntityContext.Source("Analytics"))
using (EntityContext.Partition("cold-archive"))
{
    // Reads from Analytics source, cold-archive partition
    var archivedTodos = await Todo.All(ct);
}
```

**Usage Scenarios**

- **Multi-tenant SaaS:** Isolate customer data via partitions
- **Analytics:** Route read-heavy queries to dedicated replicas
- **Caching:** Store hot data in Redis, cold data in Postgres
- **Testing:** Switch to in-memory adapters for integration tests

**Pro tip:** Partitions are lightweight and fast. Use them generously for tenant isolation, feature flags, or A/B test cohorts.

---

## 8. Transaction Coordination

**Concepts**

Sometimes you need multiple operations to succeed or fail together—creating a project with initial tasks, transferring inventory between warehouses, or importing a batch of related records.

Koan's ambient transaction support coordinates entity operations across multiple adapters with best-effort atomicity. Operations are tracked in memory and executed on commit.

**Key features:**
- Auto-commit on dispose (minimal cognitive load)
- Named transactions for telemetry correlation
- Works across different adapters (best-effort)

**Recipe**

Add transaction support in `Program.cs`:

```csharp
builder.Services.AddKoan();
builder.Services.AddKoanTransactions(options =>
{
    options.AutoCommitOnDispose = true;  // Default: auto-commit when using block exits
    options.EnableTelemetry = true;      // Activity spans + logging
    options.MaxTrackedOperations = 10_000;  // Prevent unbounded growth
});
```

**Sample**

**Basic transaction (auto-commit):**

```csharp
// Auto-commits when using block exits
using (EntityContext.Transaction("create-project"))
{
    var project = new Project { Name = "My Project" };
    await project.Save(ct);

    var task1 = new Task { ProjectId = project.Id, Title = "Setup" };
    var task2 = new Task { ProjectId = project.Id, Title = "Configure" };

    await task1.Save(ct);
    await task2.Save(ct);

    // Auto-commits here when block exits
}
```

**Explicit control (rollback on validation failure):**

```csharp
using (EntityContext.Transaction("batch-import"))
{
    var imported = new List<Product>();

    foreach (var item in importData)
    {
        var product = new Product { Name = item.Name, Price = item.Price };
        await product.Save(ct);
        imported.Add(product);
    }

    // Validation check
    var duplicates = imported.GroupBy(p => p.Name).Where(g => g.Count() > 1);
    if (duplicates.Any())
    {
        await EntityContext.RollbackAsync(ct);  // Undo everything
        throw new InvalidOperationException("Duplicate product names detected");
    }

    await EntityContext.CommitAsync(ct);  // Explicit commit
}
```

**Cross-adapter coordination:**

```csharp
// Coordinate operations across SQLite and Postgres
using (EntityContext.Transaction("sync-cache-and-db"))
{
    // Save to primary database (default adapter)
    await userData.Save(ct);

    // Also save to cache (Redis)
    using (EntityContext.Source("Cache"))
    {
        await userCache.Save(ct);
    }

    // Both commit together
    await EntityContext.CommitAsync(ct);
}
```

**Checking transaction status:**

```csharp
if (EntityContext.InTransaction)
{
    var info = EntityContext.Capabilities;
    logger.LogInformation(
        "Transaction tracking {Count} operations across {Adapters} adapter(s)",
        info.TrackedOperationCount,
        info.Adapters.Length
    );
}
```

**Usage Scenarios**

- **Multi-step creation:** Projects with initial tasks, orders with line items
- **Data migrations:** Rollback on failure for consistency
- **Cross-adapter sync:** Coordinate primary + backup storage
- **Batch imports:** All-or-nothing import with validation

**Important notes:**

⚠️ **Best-effort atomicity:** Koan provides sequential execution with error reporting, not distributed transactions. If adapter A commits and adapter B fails, A won't auto-rollback.

⚠️ **No nested transactions:** Attempting to nest throws `InvalidOperationException`.

⚠️ **Infrastructure operations bypass transactions:** `RemoveAll()` and `Truncate()` execute immediately.

⚠️ **Memory tracking:** For huge batches (10,000+ ops), break into smaller transactions to avoid memory pressure.

**Pro tip:** Use auto-commit for simple scenarios. Only reach for explicit commit/rollback when you need conditional logic mid-transaction.

---

## 9. Advanced Transfers: Copy, Move, Mirror

**Concepts**

Moving data between partitions, sources, or adapters is common: archiving old records, hydrating analytics databases, migrating tenants. Hand-written loops are error-prone and verbose.

Koan's transfer DSL provides declarative copy, move, and mirror operations:
- **Copy:** Duplicate entities to another context
- **Move:** Copy then delete from origin (strategies: AfterCopy, Batched, Synced)
- **Mirror:** Propagate records one-way or reconcile both sides; `[Timestamp]` resolves bidirectional conflicts

**Recipe**

Latest `Koan.Data.Core` (transfer builders in `Koan.Data.Core.Transfers`). Use
`Koan.Data.Abstractions.Annotations` for `[Timestamp]` conflict resolution.

**Sample**

**Copy (duplicate to another partition):**

```csharp
// Copy completed todos to archive partition
await Todo.Copy(t => t.Completed)
    .To(partition: "archive")
    .Audit(batch => logger.LogInformation("Archived {Count} todos", batch.BatchCount))
    .Run(ct);
```

**Move (transfer then delete):**

```csharp
// Move old todos from hot storage to cold
await Todo.Move()
    .WithDeleteStrategy(DeleteStrategy.Synced)  // Delete immediately after each copy
    .From(partition: "hot")
    .To(adapter: "postgres", partition: "cold")
    .Run(ct);
```

**Mirror (bidirectional sync):**

```csharp
// Keep transactional and reporting databases in sync
await Todo.Mirror(mode: MirrorMode.Bidirectional)
    .To(source: "Analytics")
    .Run(ct);

// Push production records to analytics
await Todo.Mirror(mode: MirrorMode.Push)
    .To(source: "Analytics")
    .Run(ct);
```

**Query-shaped transfer:**

```csharp
// Copy only todos with specific tag
await Todo.Copy(query => query.Where(t => t.Tags.Contains("important")))
    .To(partition: "priority")
    .Run(ct);
```

**Inspecting results:**

```csharp
var result = await Todo.Move()
    .From(partition: "temp")
    .To(partition: "archive")
    .Run(ct);

logger.LogInformation(
    "Copied {Copied}, Deleted {Deleted}, Warnings: {Warnings}",
    result.CopiedCount,
    result.DeletedCount,
    result.Warnings.Count
);

// Check for conflicts (Mirror operations)
if (result.HasConflicts)
{
    foreach (var conflict in result.Conflicts)
    {
        logger.LogWarning(
            "Conflict on {Id}: {Reason}",
            conflict.Id,
            conflict.Reason
        );
    }
}
```

**Usage Scenarios**

- **Test data:** Copy production data to staging for realistic tests
- **Archival:** Move old records to cold storage on schedule
- **Analytics:** Mirror transactional data to reporting databases
- **Migrations:** Transfer data between adapters during provider changes

Current transfer builders materialize the selected source before writing the destination in batches.
`BatchSize(...)` and `.Audit(...)` describe destination write batches; they do not bound source
materialization or provide checkpoints. If the selection can be large, use a qualified Entity stream
and an application-owned idempotency/checkpoint design instead.

**Pro tip:** `.Audit()` reports destination write batches and a final summary. It is observability,
not a checkpoint or resume token.

---

## 10. Streaming Workloads and Integration

**Concepts**

For large datasets or long-running operations, use a qualified Entity stream for an explicit
business operation, or Jobs when the work needs durable execution and retry.

**When to use:**
- **Entity stream:** Apply an application-owned, business-named operation with consumer-paced reads
- **Jobs:** Scheduled nightly transfers, archival, or DR sync

**Recipe**

Add only the pillar package that owns the capability you need:

```bash
dotnet add package Sylin.Koan.AI
dotnet add package Sylin.Koan.Jobs
```

**Sample**

**Embedding lifecycle for ordinary writes:**

```csharp
[Embedding(Async = true)]
public sealed class Recommendation : Entity<Recommendation>
{
    public string Content { get; set; } = "";
}

await recommendation.Save(ct); // persistence schedules embedding through Entity Lifecycle
```

**Explicit finite-set backfill:**

```csharp
var stale = await Recommendation.Query(
    recommendation => recommendation.NeedsReindex,
    ct);

var result = await EmbeddingMigrator.ReEmbed(stale, batchSize: 200, ct: ct);
if (!result.Success)
{
    logger.LogWarning(
        "Re-indexed {Succeeded}/{Total} recommendations",
        result.SuccessfulEntities,
        result.TotalEntities);
}
```

`ReEmbed` reports operation outcomes and does not imply collection atomicity or retry. Use
`ReEmbedAll<T>` for an intentional whole-collection model transition.

**Job for nightly archive:**

```csharp
public sealed class ArchiveOldTodosJob
    : Entity<ArchiveOldTodosJob>, IKoanJob<ArchiveOldTodosJob>
{
    public DateTimeOffset Cutoff { get; init; }

    public static async Task Execute(
        ArchiveOldTodosJob job,
        JobContext context,
        CancellationToken ct)
    {
        await Todo.Move(todo => todo.Completed && todo.CompletedDate < job.Cutoff)
            .From(partition: "active")
            .To(partition: "archive")
            .Run(ct);

        await context.Progress(1, "Archive complete");
    }
}

await new ArchiveOldTodosJob
{
    Cutoff = DateTimeOffset.UtcNow.AddDays(-90)
}.Job.Submit(ct: ct);
```

**Usage Scenarios**

- **AI lifecycle and migration:** Embed ordinary saves automatically; make explicit backfills observable
- **Nightly transfers:** Archive old data, sync replicas
- **Scheduled workflows:** Run transfers from Jobs; checkpoint and resume policy remains application-owned

---

## 11. AI and Vector Extensions

**Concepts**

Store embeddings directly on entities and integrate with shipped vector providers such as Weaviate
and Qdrant. The same `Entity<T>` patterns apply.

**Recipe**

```xml
<PackageReference Include="Koan.Data.Vector.Abstractions" Version="0.6.3" />
<PackageReference Include="Koan.Data.Vector.Connector.Weaviate" Version="0.6.3" />
```

**Sample**

```csharp
public class MediaItem : Entity<MediaItem>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public float[]? Embedding { get; set; }
}

// Generate and store embedding
var media = await MediaItem.Get(mediaId, ct);
media.Embedding = await Koan.AI.Client.Embed($"{media.Title}\n\n{media.Description}", ct);
await media.Save(ct);

// Semantic search
var similar = await MediaItem.Query("vectorDistance < 0.15", ct);
```

**For full AI/Vector guidance:** See [AI & Vector How-To](ai-vector-howto.md)

---

## 12. Troubleshooting

### Symptom: N+1 query performance issues

**Cause:** Loading entities one-by-one in a loop
**Solution:** Use batch retrieval: `await Entity.Get(ids, ct)`
**Prevention:** Code review checklist: "Any loops fetching entities? Can we batch?"

### Symptom: Out of memory during large queries

**Cause:** Loading millions of records with `.All()` or `.Query()`
**Solution:** On a qualified adapter, use provider-bounded streaming: `AllStream(batchSize: 500, ct)` or `QueryStream()`
**Prevention:** Ask: "Could this dataset grow unbounded?" Then verify the adapter advertises
`DataCaps.Query.ProviderBoundedPaging` (runtime id `query.paging.providerBounded`); InMemory, JSON, and
Redis reject streaming rather than materialize
an unbounded fallback.

### Symptom: Slow pagination on large tables

**Cause:** Using `Count.Exact()` for every page load
**Solution:** Switch to `Count.Fast()` for pagination UI
**Prevention:** Reserve `Exact` for business-critical counts

### Symptom: RemoveAll() doesn't fire hooks

**Cause:** Default `Optimized` strategy uses `Fast` on capable providers
**Solution:** Use explicit `RemoveStrategy.Safe` when hooks required
**Prevention:** Audit lifecycle hooks—if BeforeRemove does important work, always use Safe

### Symptom: Transaction doesn't rollback across adapters

**Cause:** Best-effort atomicity—not true distributed transactions
**Solution:** Design for idempotency; use compensation logic if needed
**Prevention:** Document cross-adapter transaction limitations

### Symptom: "Client evaluation" warnings in logs

**Cause:** Provider doesn't support server-side LINQ for your query
**Solution:** Simplify query or switch to provider with LINQ support (Postgres, Mongo)
**Prevention:** Test queries against production-like data volumes

### Symptom: Partition data leaking between tenants

**Cause:** Forgot to set `EntityContext.Partition()` before query
**Solution:** Use middleware or filters to set partition automatically
**Prevention:** Add integration tests validating partition isolation

### Symptom: Saved an entity but `Get()` returns null

**Cause:** The entity inherits from another concrete entity (`class Derived : SomeEntity`), so `Save` writes to the `Derived` collection while the inherited `Get` reads the base (`SomeEntity`) collection
**Solution:** Make each entity its own root and share fields via a generic base: `class Derived : SharedBase<Derived>` where `SharedBase<T> : Entity<T>` (see Section 1, "Sharing Shape Across Entities")
**Prevention:** Every concrete entity should read `: Entity<Self>` or `: SomeBase<Self>`, never `: AnotherConcreteEntity`

---

## Write-If-Changed Upserts

**Concept:** By default, `Upsert` always writes to the store and runs `AfterUpsert`, even when the
incoming data is byte-identical to what is already stored. This is correct for most scenarios. When
Lifecycle consumers perform expensive follow-up work, an unchanged re-crawl can create needless
work. `UpsertIfChanged` compares first and enters the normal Upsert/Lifecycle path only when the model
differs from its stored value.

**Semantics:**
- The current stored application value and caller's model are compared before Lifecycle.
- If they are byte-identical, persistence and all Upsert Lifecycle phases are skipped; the method returns `false`.
- If they differ, the normal canonical `Upsert` runs, including cancellation and all configured handlers.
- If the entity is new (no prior exists), or if the serialization fails for any reason, the write always proceeds (safe fallback).
- The method returns `true` when written, `false` when skipped.

**Recipe:**

```csharp
// opt in -- call instead of Upsert when skipping no-op writes matters
var written = await Article.UpsertIfChanged(article);
if (written)
{
    // downstream events were fired, cache was invalidated, etc.
}

// partition-aware variant
var written = await Article.UpsertIfChanged(article, tenantPartition);
```

**When to use:**
- Crawlers or sync pipelines that re-emit every record even when unchanged.
- Event-driven consumers where `AfterUpsert` triggers expensive fan-out (cache invalidation, notifications, indexing).
- High-frequency polling loops where most updates are no-ops.

**When NOT to use:**
- When `AfterUpsert` must always fire regardless of content (e.g., heartbeat updates, explicit re-triggers).
- When the entity has fields the serializer cannot capture faithfully (custom converters, computed properties set externally). In that case, `Upsert` is the safe default.

**Cost:** `UpsertIfChanged` always loads the prior before deciding whether to enter Lifecycle and write.
This is one extra read per call. On hot paths that almost always write, prefer `Upsert`.

---

## Next Steps

You've learned the fundamentals—from simple saves to capability-qualified streaming and transactions.
Here's where to go next:

**Immediate next actions:**
1. Try batch retrieval in your next API endpoint (replace loops with `Entity.Get(ids)`)
2. Add Fast counts to your dashboard (see the 1000x speedup)
3. On a qualified adapter, stream the Entity source for your next large export

**Explore related capabilities:**
- **Semantic search?** → [AI & Vector How-To](ai-vector-howto.md) (embeddings, hybrid search)
- **Multi-source deduplication?** → [Canon Capabilities](canon-capabilities-howto.md) (aggregation, identity graphs)
- **Partial updates?** → [Patch Capabilities](patch-capabilities-howto.md) (RFC 6902, merge-patch)

**Real-world examples:**
- `samples/journeys/GardenCoop/01-GardenJournal` - sensor binding and watering-reminder automation
- `samples/journeys/GardenCoop/02-LocalDiscovery` - the same application plus local Entity embedding and vector search

**Keep exploring:**
- Combine Flow + Jobs with transfers for safe large-scale migrations
- Add lifecycle hooks to enforce business rules
- Use context routing for multi-tenant isolation

When in doubt, remember: Koan's entity-first patterns keep your code declarative, provider-agnostic, and ready to scale. Start simple, add complexity only when you need it.

Happy building! 🚀

---

**Validation status:** Not yet tested end-to-end
**Streaming validation:** SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, and Couchbase qualify;
InMemory, JSON, and Redis fail closed before query or yield.
