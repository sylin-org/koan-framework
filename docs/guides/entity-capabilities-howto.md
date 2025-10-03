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

# Koan Entity Capabilities – End-to-End How-To

This guide walks through everything the Koan data pillar offers, starting with a single `todo.Save()` and ending with multi-provider mirroring, Flow pipelines, and vector exports. Each block grows in sophistication, lists **concepts**, a **recipe** (packages/config you need) and usage **scenarios** to illustrate the benefits. Examples draw from:

- **g1c1 GardenCoop** – beginner-friendly domain, lifecycle automation, streaming.
- **S5.Recs** – multi-partition recommendations, Flow/Jobs orchestration, vector usage.
- **S14.AdapterBench** – multi-provider benchmarking now powered by transfer DSL.

## 0. Prerequisites

1. Add the Koan baseline packages:
   ```xml
   <PackageReference Include="Koan.Core" Version="0.6.2" />
   <PackageReference Include="Koan.Data.Core" Version="0.6.2" />
   <PackageReference Include="Koan.Data.Abstractions" Version="0.6.2" />
   ```
2. Reference at least one data adapter (SQLite below as an example) and configure the default source:
   ```xml
   <PackageReference Include="Koan.Data.Connector.Sqlite" Version="0.6.2" />
   ```
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
3. Boot the runtime with `builder.Services.AddKoan();` in your `Program.cs`.

With that in place, you can lean on everything described below.

---

## 1. Foundations – Defining & Saving Entities

**Concepts**

- `Entity<T>` provides auto GUID v7 IDs, instance `Save`/`Remove`, static helpers (`Get`, `All`, `RemoveAll`).
- Everything routes through the default source configured in app settings.

**Recipe**

- Packages already listed in prerequisites.
- No special configuration beyond `Koan:Data:Sources:Default`.

**Sample**

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

var todo = new Todo { Title = "Plant strawberries" };
await todo.Save();          // persists via default adapter

var fetched = await Todo.Get(todo.Id);
await fetched!.Remove();
```

**Usage scenarios & benefits**

- *GardenCoop* seeds initial gardening tasks without any DbContext or repository plumbing, keeping focus on domain logic.
- Architects get immediate provider neutrality—swap the adapter in configuration and the entity code stays untouched.

**Going further – custom keys**

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

- LINQ (`Query`, `QueryWithCount`), string queries, paging helpers (`FirstPage`, `Page`).
- Streaming (`AllStream`, `QueryStream`) yields `IAsyncEnumerable` for large datasets.
- Providers that lack server-side LINQ fall back to client evaluation (Koan warns you).

**Recipe**

- Same packages as foundations.
- Prefer adapters that implement `ILinqQueryRepository` (Postgres, Mongo) for server-side filters.

**Sample**

```csharp
var overdue = await Todo.Query(t => !t.Completed && t.DueDate < DateTimeOffset.UtcNow);
var secondPage = await Todo.Page(page: 2, size: 20);

await foreach (var reading in Reading.QueryStream("plot == "A1"", batchSize: 200, ct))
{
    Process(reading);
}
```

**Usage scenarios & benefits**

- *GardenCoop* uses streaming to analyze months of moisture readings without exhausting memory.
- *S5.Recs* paginates personalized suggestions while providing total counts for UI pagination controls.

**Advanced example – QueryWithCount and options**

```csharp
var result = await Todo.QueryWithCount(
    t => t.ProjectId == projectId,
    new DataQueryOptions(orderBy: nameof(Todo.Created), descending: true),
    ct);

Console.WriteLine($"Showing {result.Items.Count} of {result.TotalCount}");
```

---

## 3. Batch Operations & Lifecycle Hooks

**Concepts**

- `List<T>.Save()` bulk persistence; `Entity.Batch()` combines adds/updates/deletes.
- Lifecycle hooks (`ProtectAll`, `BeforeUpsert`, `AfterLoad`) keep invariants and projections near the data.

**Recipe**

- Dependencies already covered; lifecycle API lives in `Koan.Data.Core.Events` (included with `Koan.Data.Core`).

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

**Lifecycle example**

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

**Usage scenarios & benefits**

- *GardenCoop* enforces that every reminder has a title and automatically formats display text.
- *S5.Recs* bulk imports thousands of recommendations in a single batch call to prep nightly jobs.

---

## 4. Context Routing – Partitions, Sources, Adapters

**Concepts**

- **Partition:** logical suffix (`Todo#archive`) for per-cohort isolation.
- **Source:** named configuration (`Koan:Data:Sources:{name}`) that picks adapter + connection string.
- **Adapter:** explicit provider override (`EntityContext.Adapter("mongo")`).
- **Rule:** Source XOR Adapter (ADR DATA-0077). Each scope is ambient (AsyncLocal) and replaced by nested scopes.

**Recipe**

- Reference adapter packages for each provider you want to target (e.g., `Koan.Data.Connector.Postgres`, `Koan.Data.Connector.Mongo`).
- Configure `Koan:Data:Sources` accordingly.

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

**Usage scenarios & benefits**

- *S5.Recs* isolates tenant recommendations via partitions (`Recommendation#tenant-alpha`).
- Analytics queries route to dedicated Postgres sources without touching transactional stores.

**Advanced scope nesting**

```csharp
using (EntityContext.Source("archive"))
using (EntityContext.Partition("cold"))
{
    await Todo.Copy().To(partition: "cold-snapshot").Run();
}
```

---

## 5. Advanced Transfers – Copy, Move, Mirror

**Concepts**

- `Copy()` clones entities into another context.
- `Move()` clones then deletes from origin (strategies: `AfterCopy`, `Batched`, `Synced`).
- `Mirror()` synchronizes data in one or both directions; `[Timestamp]` resolves conflicts.
- `.Audit()` receives per-batch telemetry; `TransferResult<TKey>` summarizes counts, warnings, conflicts.

**Recipe**

- Latest `Koan.Data.Core` (transfer builders live in `Koan.Data.Core.Transfers`).
- `System.ComponentModel.DataAnnotations` when using `[Timestamp]`.
- Adapters for origin and destination contexts.
- Optional logging for `.Audit`.

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

**Usage scenarios & benefits**

- *S14.AdapterBench* uses `Copy()` to preload providers and `Mirror()` to sync benchmark artifacts back into the default store without hand-written loops.
- Ops teams move cold data into cheaper storage overnight with `Move()` and a specific delete strategy.
- Bidirectional `Mirror()` keeps reporting and transactional stores aligned while surfacing conflicts when timestamps are missing.

**Query-shaped transfer**

```csharp
await Todo.Copy(query => query.Where(t => t.Tags.Contains("ops")))
         .To(source: "analytics")
         .Run();
```

**Inspecting results**

```csharp
var result = await Todo.Move().To(partition: "cold").Run();
logger.LogInformation("Copied {Copied}/Deleted {Deleted}", result.CopiedCount, result.DeletedCount);
result.Audit.Last().IsSummary.Should().BeTrue();
```

---

## 6. Streaming Workloads, Flow & Jobs

**Concepts**

- Use `AllStream`/`QueryStream` in Koan Flow for large data pipelines.
- Long-running or scheduled transfers belong in Koan Jobs for checkpointing and retries.

**Recipe**

- Add `Koan.Flow` package for pipeline DSL.
- Add `Koan.Jobs.Core` when you need resumable or scheduled execution.
- Ensure adapters support efficient paging for streaming.

**Sample – Flow pipeline (S5)**

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

**Sample – Job for nightly archive**

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

**Usage scenarios & benefits**

- *S5.Recs* streams recommendation updates to Flow, generating embeddings at scale without full materialization.
- Jobs provide reliable scheduling and resumption for nightly transfers or DR syncs.

---

## 7. AI & Vector Extensions

**Concepts**

- Store embeddings directly on entities, integrate with vector providers, export caches (ADR-0051).
- Works with the same `Entity<T>` patterns.

**Recipe**

- Reference `Koan.Data.Vector.Abstractions` and the specific connector (e.g., `Koan.Data.Vector.Connector.Weaviate`).
- Configure vector source(s) in settings.

**Sample**

```csharp
public class Recommendation : Entity<Recommendation>
{
    public float[]? Embedding { get; set; }
}

var matches = await Recommendation.Query("vectorDistance < 0.15");
```

**Usage scenarios & benefits**

- *S5.Recs* exports vector embeddings into a cache via `Copy()` so APIs can respond instantly.
- Architects can swap vector backends (Weaviate, Pinecone, etc.) by changing configuration, no application code changes.

---

## 8. Observability & Testing

**Concepts**

- `TransferResult<TKey>` surfaces counts, warnings, conflicts, audit batches.
- BootReport adds module notes for introspection.
- `Koan.Testing` simplifies verifying context routing and partitions.

**Recipe**

- Logging via `Microsoft.Extensions.Logging`.
- Optional: BootReport consumers for module diagnostics.
- Add `Koan.Testing` to test projects.

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

**Usage scenarios & benefits**

- QA teams examine audit summaries to ensure Move operations deleted exactly what was copied.
- Architects rely on BootReport output to confirm sources/partitions are configured as expected during boot.

---

## 9. Deployment Readiness

**Concepts**

- Seeding/cleanup using `RemoveAll` per partition/source.
- Schema guard (`EntitySchemaGuard`) for health checks.
- Koan Jobs for durable, resumable transfers.

**Recipe**

- Same packages; add jobs & health check packages if desired.
- Configure health checks in host builder.

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

**Usage scenarios & benefits**

- Teams wipe and reseed tenants during staging deploys without hand-written scripts.
- Health checks detect missing indexes or migrations before traffic hits the service.
- Long-running archive jobs leverage Koan Jobs for retry and progress tracking.

---

## Next Steps

1. Explore the referenced samples (`samples/guides/g1c1.GardenCoop`, `samples/S5.Recs`, `samples/S14.AdapterBench`) to see these concepts in action.
2. Extend the transfer DSL in your domain—add `.Mirror()` runs before cut-overs, or `Copy()` recipes to hydrate analytics sources.
3. Combine Flow + Jobs with the transfer DSL to orchestrate large data migrations safely.

When in doubt, stick to the entity-first patterns above. They keep your code declarative, provider-agnostic, and ready for Koan’s automation pillars.
