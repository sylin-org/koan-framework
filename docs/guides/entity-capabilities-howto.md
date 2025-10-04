---
type: GUIDE
domain: data
title: "Entity Capabilities How-To"
audience: [developers, architects]
status: draft
last_updated: 2025-10-02
framework_version: v0.6.2
validation:
  status: not-yet-tested
  scope: docs/guides/entity-capabilities-howto.md
---

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

Packages already listed in prerequisites. No special configuration beyond `Koan:Data:Sources:Default`.

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

**Usage Scenarios**

Applications seed initial data without DbContext or repository plumbing, keeping focus on domain logic. Architects get immediate provider neutrality: swap the adapter in configuration, entity code stays untouched.

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

## 5. Advanced Transfers: Copy, Move, Mirror

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

## 6. Streaming Workloads, Flow and Jobs

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

## 7. AI and Vector Extensions

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

## 8. Observability and Testing

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

## 9. Deployment Readiness

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
