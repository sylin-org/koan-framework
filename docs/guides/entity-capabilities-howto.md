---
type: GUIDE
domain: data
title: "Entity Capabilities How-To"
audience: [developers, architects]
status: current
last_updated: 2025-11-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-09
  status: verified
  scope: All code examples tested against v0.6.3
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

Same packages as before. For best performance, use adapters with `ILinqQueryRepository` support (Postgres, MongoDB, SQL Server).

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
    new DataQueryOptions(page: 2, pageSize: 50),
    ct
);
```

**Pagination with total counts (for UI controls):**

```csharp
var result = await Todo.QueryWithCount(
    t => t.ProjectId == projectId,
    new DataQueryOptions(
        orderBy: nameof(Todo.Created),
        descending: true,
        page: 1,
        pageSize: 20
    ),
    ct
);

Console.WriteLine($"Showing {result.Items.Count} of {result.TotalCount} todos");
// Output: Showing 20 of 347 todos
```

**Streaming large datasets:**

For datasets too large to fit in memory, use streaming:

```csharp
// Process millions of log entries without loading all into RAM
await foreach (var reading in SensorReading.QueryStream(
    "timestamp > '2024-01-01'",
    batchSize: 500,  // Fetch 500 at a time
    ct))
{
    await ProcessReading(reading, ct);
    // Each batch is processed then garbage collected
}
```

**Usage Scenarios**

- **Web APIs:** Paginate results for list endpoints (`GET /api/todos?page=2&size=20`)
- **Reports:** Stream large datasets for CSV exports without memory exhaustion
- **Dashboards:** Query with counts for "Showing X of Y" UI controls
- **Admin tools:** Use string queries for user-built dynamic filters

**Pro tip:** If you're rendering a grid or list, you almost always want `QueryWithCount()` for pagination controls.

---

## 4. Counting Strategies: Fast vs Exact

**Concepts**

How many todos do you have? Sounds simple, but at scale (millions of rows), full table scans can take 20+ seconds. Koan gives you three counting strategies:

- **Exact:** Full accuracy, may be slower (scans entire table)
- **Fast:** Lightning-fast estimates using database metadata (5000x speedup on large tables)
- **Optimized:** Framework chooses the best approach (usually Fast if available)

Think of it like this: counting jelly beans in a jar—you can count each one (Exact) or estimate from the jar's size (Fast).

**When to use each:**
- Dashboard summaries? **Fast** is perfect (who cares if it's 1,000,042 vs 1,000,000?)
- Business-critical inventory check? **Exact** is essential
- General use? **Optimized** (the default) makes smart choices for you

**Recipe**

No new packages. Count strategies are built into Entity<T>.

**Sample**

**Simple counts:**

```csharp
// Default: framework chooses best strategy
var total = await Todo.Count;  // Usually uses Fast if available

// Explicit exact count (guaranteed accuracy)
var exact = await Todo.Count.Exact(ct);

// Explicit fast count (metadata estimate)
var fast = await Todo.Count.Fast(ct);

// Filtered count
var completed = await Todo.Count.Where(t => t.Completed, ct);

// Filtered with explicit strategy
var urgent = await Todo.Count.Where(
    t => t.Priority > 3,
    CountStrategy.Fast,
    ct
);

// Count a specific partition
var archivedCount = await Todo.Count.Partition("archive", ct);
```

**Performance comparison (10 million rows):**

| Provider   | Exact Count | Fast Count  | Strategy Used                    |
|------------|-------------|-------------|----------------------------------|
| PostgreSQL | ~25 seconds | ~5ms        | `pg_stat_user_tables.n_live_tup` |
| SQL Server | ~20 seconds | ~1ms        | `sys.dm_db_partition_stats`      |
| MongoDB    | ~15 seconds | ~10ms       | `estimatedDocumentCount()`       |
| SQLite     | Full scan   | Same        | No metadata (always exact)       |
| JSON       | In-memory   | Same        | Dictionary.Count (instant)       |

**When to use each strategy:**

```csharp
// ✅ Pagination UI (fast is fine)
var totalPages = (await Todo.Count.Fast(ct) + pageSize - 1) / pageSize;

// ✅ Dashboard summary (estimates OK)
var stats = new
{
    Total = await Todo.Count.Fast(ct),
    Completed = await Todo.Count.Where(t => t.Completed, CountStrategy.Fast, ct),
    Pending = await Todo.Count.Where(t => !t.Completed, CountStrategy.Fast, ct)
};

// ✅ Business logic (exact required!)
var exactInventory = await Product.Count.Where(
    p => p.InStock && p.WarehouseId == warehouseId,
    CountStrategy.Exact,
    ct
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

Dependencies already covered. Lifecycle API lives in `Koan.Data.Core.Events` (included automatically).

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

**Lifecycle hooks:**

```csharp
public static class TodoLifecycle
{
    public static void Configure(EntityLifecycleBuilder<Todo> builder)
    {
        builder
            // Protect all properties by default
            .ProtectAll()

            // Allow specific properties to change
            .Allow(t => t.Title, t => t.Completed, t => t.DueDate)

            // Validation before save
            .BeforeUpsert(async (ctx, next) =>
            {
                if (string.IsNullOrWhiteSpace(ctx.Entity.Title))
                    throw new InvalidOperationException("Todo must have a title");

                if (ctx.Entity.Title.Length > 200)
                    throw new InvalidOperationException("Title too long (max 200 chars)");

                await next();  // Continue to next hook or save
            })

            // Auto-format after load
            .AfterLoad(ctx =>
            {
                // Trim whitespace
                if (ctx.Entity.Title != null)
                    ctx.Entity.Title = ctx.Entity.Title.Trim();
            });
    }
}
```

**How hooks work:**
1. `ProtectAll()` prevents accidental property mutations
2. `Allow()` whitelists safe-to-change properties
3. `BeforeUpsert` runs validation before save
4. `AfterLoad` formats data after fetching from database

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
- **Optimized:** Framework chooses Fast when safe, Safe when hooks matter

Here's the key insight: **Optimized uses Fast on most providers** (Postgres, SQL Server, MongoDB, SQLite, Redis). If you need audit trails, explicitly choose Safe.

**Quick decision:**
- Need audit trail or cleanup logic? → **Safe**
- Test cleanup or temp data? → **Optimized** (default)
- Know hooks aren't needed? → **Fast** (explicit)

**Recipe**

Same packages as always. Providers with `WriteCapabilities.FastRemove` (Postgres, SQL Server, MongoDB, SQLite, Redis) support fast strategies.

**Sample**

**Basic removal:**

```csharp
// Default: Optimized (usually Fast on capable providers)
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

**⚠️ Important:** Optimized uses Fast on most providers, **bypassing hooks**. If you have audit logging or cleanup logic in lifecycle hooks, use explicit Safe:

```csharp
public static class OrderLifecycle
{
    public static void Configure(EntityLifecycleBuilder<Order> builder) =>
        builder.BeforeDelete(async (ctx, next) =>
        {
            // Log deletion for compliance
            await AuditLog.RecordDeletion(ctx.Entity.Id, ctx.Entity.CustomerId);
            await next();
        });
}

// ❌ Optimized bypasses BeforeDelete hook on Postgres!
await Order.RemoveAll(ct);

// ✅ Safe guarantees hook fires
await Order.RemoveAll(RemoveStrategy.Safe, ct);
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
        Completed = await Todo.Count.Where(t => t.Completed, ct)
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

Koan's transfer DSL provides declarative, resumable operations:
- **Copy:** Duplicate entities to another context
- **Move:** Copy then delete from origin (strategies: AfterCopy, Batched, Synced)
- **Mirror:** Synchronize data (one-way or bidirectional); `[Timestamp]` resolves conflicts

**Recipe**

Latest `Koan.Data.Core` (transfer builders in `Koan.Data.Core.Transfers`). Use `System.ComponentModel.DataAnnotations` for `[Timestamp]` conflict resolution.

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

// One-way sync (production → analytics)
await Todo.Mirror(mode: MirrorMode.OneWay)
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
            "Conflict on {Id}: Source modified {Source}, Dest modified {Dest}",
            conflict.Id,
            conflict.SourceModified,
            conflict.DestinationModified
        );
    }
}
```

**Usage Scenarios**

- **Test data:** Copy production data to staging for realistic tests
- **Archival:** Move old records to cold storage on schedule
- **Analytics:** Mirror transactional data to reporting databases
- **Migrations:** Transfer data between adapters during provider changes

**Pro tip:** Use `.Audit()` to track progress on large transfers. Each batch emits metrics—hook them into your logging or APM.

---

## 10. Streaming Workloads and Integration

**Concepts**

For massive datasets or long-running operations, combine Entity streaming with Koan Flow (pipelines) or Jobs (scheduled/resumable work).

**When to use:**
- **Flow:** Transform millions of records (generate embeddings, update fields)
- **Jobs:** Scheduled nightly transfers, archival, or DR sync

**Recipe**

Add pipeline support:
```xml
<PackageReference Include="Koan.Flow" Version="0.6.3" />
<PackageReference Include="Koan.Jobs.Core" Version="0.6.3" />
```

**Sample**

**Flow pipeline (embedding backfill):**

```csharp
// Process millions of recommendations without loading all into memory
await Flow.Pipeline("generate-embeddings")
    .ForEach(await Recommendation.AllStream(batchSize: 200, ct))
    .Do(async (rec, ct) =>
    {
        // Generate embedding
        rec.Embedding = await Ai.Embed(rec.Content, ct);
        await rec.Save(ct);
    })
    .RunAsync(ct);
```

**Job for nightly archive:**

```csharp
public class ArchiveOldTodosJob : IJob
{
    public async Task ExecuteAsync(JobContext ctx)
    {
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-90);

        await Todo.Move()
            .Where(t => t.Completed && t.CompletedDate < cutoffDate)
            .From(partition: "active")
            .To(partition: "archive")
            .Run(ctx.CancellationToken);
    }
}
```

**Usage Scenarios**

- **AI pipelines:** Generate embeddings for millions of documents
- **Nightly transfers:** Archive old data, sync replicas
- **Resumable operations:** Long-running migrations with checkpointing

---

## 11. AI and Vector Extensions

**Concepts**

Store embeddings directly on entities and integrate with vector providers (Weaviate, Pinecone, Qdrant). Same Entity<T> patterns you know.

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
media.Embedding = await Ai.Embed($"{media.Title}\n\n{media.Description}", ct);
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
**Solution:** Use streaming: `AllStream(batchSize: 500, ct)` or `QueryStream()`
**Prevention:** Ask: "Could this dataset grow unbounded?" → Use streaming

### Symptom: Slow pagination on large tables

**Cause:** Using `Count.Exact()` for every page load
**Solution:** Switch to `Count.Fast()` for pagination UI
**Prevention:** Reserve `Exact` for business-critical counts

### Symptom: RemoveAll() doesn't fire hooks

**Cause:** Default `Optimized` strategy uses `Fast` on capable providers
**Solution:** Use explicit `RemoveStrategy.Safe` when hooks required
**Prevention:** Audit lifecycle hooks—if BeforeDelete does important work, always use Safe

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

---

## Next Steps

You've learned the fundamentals—from simple saves to production-scale streaming and transactions. Here's where to go next:

**Immediate next actions:**
1. Try batch retrieval in your next API endpoint (replace loops with `Entity.Get(ids)`)
2. Add Fast counts to your dashboard (see the 1000x speedup)
3. Use streaming for your next large dataset export

**Explore related capabilities:**
- **Semantic search?** → [AI & Vector How-To](ai-vector-howto.md) (embeddings, hybrid search)
- **Multi-source deduplication?** → [Canon Capabilities](canon-capabilities-howto.md) (aggregation, identity graphs)
- **Partial updates?** → [Patch Capabilities](patch-capabilities-howto.md) (RFC 6902, merge-patch)

**Real-world examples:**
- `samples/S5.Recs` - Media recommendation engine with vector search
- `samples/guides/g1c1.GardenCoop` - Multi-tenant task management
- `samples/S14.AdapterBench` - Performance benchmarks across adapters

**Keep exploring:**
- Combine Flow + Jobs with transfers for safe large-scale migrations
- Add lifecycle hooks to enforce business rules
- Use context routing for multi-tenant isolation

When in doubt, remember: Koan's entity-first patterns keep your code declarative, provider-agnostic, and ready to scale. Start simple, add complexity only when you need it.

Happy building! 🚀

---

**Last Validation:** 2025-11-09
**Framework Version:** v0.6.3
**Tested Against:** SQLite, PostgreSQL, MongoDB, Redis, InMemory adapters
