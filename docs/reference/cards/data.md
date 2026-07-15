---
type: REF
domain: data
title: "Data — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/data.md
---

# Data — pillar map

> One-screen map of the Data pillar — the framework's strongest, most-used surface. Full detail: [data/index.md](../data/index.md).

**What it does** — Entity-first persistence across SQL / document / JSON / vector stores behind **one** model. The same `Todo.Save()` / `Todo.Query(...)` code runs on any provider; the adapter is chosen by package reference + config, and the [ARCH-0084](../../decisions/ARCH-0084-unified-capability-model.md) capability model decides per-call whether a query is pushed down to the store or evaluated in-memory — no adapter carries fallback logic.

## The one canonical pattern

Derive from `Entity<T>` (GUID v7 ids auto-generated); use the static + instance verbs. Canonical verbs are **Save / Remove / Query** ([principles.md §5](../../architecture/principles.md)).

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

var todo = new Todo { Title = "Buy milk" };
await todo.Save();                            // create or update
var one  = await Todo.Get(todo.Id);           // by id
var open = await Todo.Query(t => !t.Done);    // predicate query (pushed down when the adapter can)
await foreach (var t in Todo.AllStream()) { } // bounded pages on a qualified adapter
await todo.Remove();                          // delete
```

`AllStream` and `QueryStream` require provider-bounded paging. SQLite, PostgreSQL, SQL Server,
CockroachDB, MongoDB, and Couchbase are qualified; InMemory, JSON, and Redis reject correctively
before query/yield. See the [streaming guide](../../guides/data/entity-access-and-streaming.md).

Use `Entity<T, TKey>` when the key isn't a string. Batch writes: `Todo.UpsertMany(items)`.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[Identifier]` | Mark the id property when it isn't the inherited `Id`. |
| `[Index]` · `[Index(Group="ix", Order=N)]` · `[Index(Ttl=true)]` | Secondary, composite, and time-to-live indexes. |
| `[Parent(typeof(Category))]` | Declare a relationship edge; enables `.Parent()` / `.Children()` navigation + batch loading. |
| `[Timestamp]` · `[Timestamp(OnSave=true)]` | Set-once created / every-save modified stamps. |
| `[DataAdapter("sqlite")]` | Route this entity to a specific provider (`"json"`, `"mongo"`, `"postgres"`, …). |

## The escape hatch

Raw SQL without leaving Koan — `IDataService.Direct(...)`, folded into `Koan.Data.Core` and registered by default ([ARCH-0090 §1](../../decisions/ARCH-0090-auth-data-surface-trim.md)):

```csharp
var rows = await data.Direct(adapter: "sqlite")
    .Query<Row>("SELECT id, title FROM Todo WHERE done = 0");
```

Source XOR adapter; parameterized; transactions via `.Begin()`. The adapter's connection factory is present only when you reference that adapter (Reference = Intent).

## The sample that shows it

[`samples/S1.Web`](../../../samples/S1.Web/README.md) — `Entity<Todo>` CRUD plus the relationship system (`[Parent]` navigation, batch loading, streaming) over a minimal web UI.
