---
name: koan-data-modeling
description: Aggregate boundaries, [Parent] relationships + GetRelatives/Relatives navigation, lifecycle events (Setup/BeforeUpsert/BeforeRemove), value objects, timestamps, context routing
pillar: data
card: docs/reference/cards/data.md
status: current
last_validated: 2026-06-18
---

# Koan Data Modeling

## Trigger this skill when you see

- Aggregate design — business invariants, value objects (records), domain methods on an `Entity<T>`
- `[Parent(typeof(...))]` on an FK property, `entity.GetRelatives(ct)`, `collection.Relatives<T,K>(ct)`, N+1 enrichment
- Lifecycle wiring — `Entity<T>.Events.Setup(...)` / `.BeforeUpsert(...)` / `.BeforeRemove(...)` / `.AfterLoad(...)`, `ctx.Current`, `ctx.Proceed()` / `ctx.Cancel(...)`, `ctx.ProtectAll()` / `ctx.AllowMutation(...)`
- `[Timestamp]` / `[Timestamp(OnSave = true)]`, `[Index]`, `[DataAdapter("...")]` routing
- `EntityContext.Partition(...)` / `.Source(...)` / `.Adapter(...)` scoping
- "cascade delete", "soft delete", "audit trail", "validation hook", "relationship", "parent/child"

## Core principle

**An entity is the aggregate.** Encapsulate invariants on the type, model cohesive data as `record` value objects, declare relationships with `[Parent(typeof(...))]` (the FK string property carries the edge), and enforce rules in **lifecycle events** registered through `Entity<T>.Events` — not in scattered call sites. Navigation is first-class: `GetRelatives` / `Relatives<T,K>` batch-load the graph instead of hand-rolled FK helpers ([DATA-0072](../../../docs/decisions/DATA-0072-parent-relationship-attribute-explicit-type.md)). Timestamps are opt-in via `[Timestamp]`.

<!-- validate -->
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Extensions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

public record Money(decimal Amount, string Currency);   // cohesive value object

public sealed class Customer : Entity<Customer>
{
    public string Name { get; set; } = "";
}

public sealed class Order : Entity<Order>
{
    [Parent(typeof(Customer))]                          // FK edge → enables GetRelatives / Relatives
    public string CustomerId { get; set; } = "";

    public Money Total { get; set; } = new(0m, "USD");
    public bool Shipped { get; set; }

    [Timestamp] public DateTimeOffset CreatedAt { get; set; }                 // set-once
    [Timestamp(OnSave = true)] public DateTimeOffset UpdatedAt { get; set; }  // every save
}

public static class OrderRules
{
    // Register lifecycle events once at startup (e.g. from a KoanModule.Start).
    public static void Configure()
    {
        Order.Events.BeforeUpsert(ctx =>                // sync overload returns EntityEventResult
        {
            return ctx.Current.Total.Amount < 0m
                ? ctx.Cancel("Total cannot be negative", code: "ORDER_TOTAL_NEGATIVE")
                : ctx.Proceed();
        });

        Order.Events.Setup(ctx => { ctx.ProtectAll(); ctx.AllowMutation(nameof(Order.Shipped)); });
    }
}

public sealed class OrderService
{
    public async Task<Customer?> OwnerOf(Order order, CancellationToken ct = default)
    {
        var graph = await order.GetRelatives(ct);        // RelationshipGraph<Order>
        return graph.Parents.TryGetValue(nameof(Order.CustomerId), out var p) ? p as Customer : null;
    }

    public async Task<IReadOnlyList<Order>> RecentEnriched(int days, CancellationToken ct = default)
    {
        var orders = await Order.Query(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-days), ct);
        await orders.Relatives<Order, string>(ct);       // ONE batched load, not N round-trips
        return orders;
    }

    public bool CanPushDown()
        => Data<Order, string>.Capabilities.Has(DataCaps.Query.Linq);   // ARCH-0084 capability gate
}
```

## Modeling surface (Reference = Intent)

| Declare this | Effect |
|---|---|
| `[Parent(typeof(Customer))]` on a `string` FK property | Declares a relationship edge; powers `GetRelatives` / `Relatives<T,K>` + batch loading ([DATA-0072](../../../docs/decisions/DATA-0072-parent-relationship-attribute-explicit-type.md)). |
| `Entity<T>.Events.Setup / BeforeUpsert / BeforeRemove / AfterLoad` | Lifecycle hooks; `ctx.Current` is the entity, return `ctx.Proceed()` / `ctx.Cancel(reason, code)`. |
| `ctx.ProtectAll()` + `ctx.AllowMutation(name)` | Field-level write guard inside a `Setup` hook — protect everything, allow a whitelist. |
| `[Timestamp]` · `[Timestamp(OnSave = true)]` (`Koan.Data.Abstractions.Annotations`) | Opt-in set-once / every-save `DateTimeOffset` stamps — **not** inherited. |
| `[Index]` · `[Index(Group="ix", Order=N)]` · `[Index(Ttl=true)]` | Secondary, composite, and TTL indexes. |
| `[DataAdapter("mongo")]` on an entity | Routes one entity to a named provider when several are referenced. |
| `record Money(decimal Amount, string Currency)` | Value object — immutable, cohesive, serialized inline with the aggregate. |

## Relationships: first-class navigation (DATA-0072)

The FK is a plain `string` property tagged `[Parent(typeof(Parent))]`. From there:

- `await entity.GetRelatives(ct)` → `RelationshipGraph<T>` for a single entity (`.Parents[fkPropertyName]`, `.Children[childTypeName][refProperty]`).
- `await collection.Relatives<T, K>(ct)` → batches the whole set through `BatchRelationshipLoader` (100-item batches) — the N+1 cure. The same extension exists for `IAsyncEnumerable<T>` to enrich a stream.

Do **not** hand-roll `GetUser()` / `GetTodos()` FK helpers or loop `await Get(id)` per row — that is the N+1 anti-pattern the relationship system replaces.

## Lifecycle events: invariants in one place

`Entity<T>.Events` is the registration surface (register once at boot). Handlers receive `EntityEventContext<T>`:

- `ctx.Current` — the entity flowing through the pipeline (**not** `ctx.Entity`).
- `BeforeUpsert` / `BeforeRemove` must return an `EntityEventResult`: `ctx.Proceed()` to continue, `ctx.Cancel(reason, code)` to veto (raises `EntityEventCancelledException`). Async overloads return `ValueTask<EntityEventResult>`.
- `Setup` runs once per operation to seed protection: `ctx.ProtectAll()` then `ctx.AllowMutation(name)` for the writable whitelist.
- `AfterLoad` — post-read shaping (computed/formatted fields).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `EntityLifecycleBuilder<T>` / `Configure(pipeline)` / `.Allow(...)` / `(ctx, next) => await next()` | None of these exist — use `Entity<T>.Events.Setup/BeforeUpsert/BeforeRemove`; protect via `ctx.ProtectAll()` + `ctx.AllowMutation(name)`. |
| `ctx.Entity` inside a hook | `ctx.Current` — that's the property name. |
| `throw new InvalidOperationException(...)` to veto a save | `return ctx.Cancel(reason, code)` — the framework raises the cancellation; vetoes stay declarative. |
| Hand-rolled `GetUser()` / `GetItems()` FK navigation helpers | `[Parent(typeof(...))]` + `entity.GetRelatives(ct)` (DATA-0072 is the first-class nav). |
| `foreach (var o in orders) await o.GetCustomer()` (N+1) | `await orders.Relatives<Order, string>(ct)` — one batched load. |
| `Task<List<Order>>` as a facade return type | `IReadOnlyList<Order>` — facade and relationship loads return read-only lists. |
| `Query().Where(o => ...)` (parameterless then filter) | `Order.Query(o => ..., ct)` — the predicate is the argument. |
| "`CreatedAt` is filled automatically" | Only if you declare a `DateTimeOffset` with `[Timestamp]` / `[Timestamp(OnSave = true)]`. |
| `Capabilities.HasFlag(QueryCaps...)` / `EntityCaps` | `Data<Order, string>.Capabilities.Has(DataCaps.Query.Linq)` (ARCH-0084 removed the flag enums). |

## Escape hatches

- **Context routing (DATA-0077)**: scope a block with `using (EntityContext.Partition("tenant-a"))`, `.Source("eu")`, or `.Adapter("mongo")`. **Source XOR Adapter** — supplying both is a hard constructor throw, not undefined behavior. Partition names are validated (`PartitionNameValidator`).
- **Fast bulk delete**: `Order.RemoveAll(RemoveStrategy.Fast, ct)` when `Order.SupportsFastRemove` (truncate-style); otherwise `RemoveStrategy.Optimized` is the default.
- **Pagination/sort**: `QueryDefinition.All.WithSort<Order>(s => s.OrderByDescending(o => o.CreatedAt)).WithPagination(page, size)`, then `Order.QueryWithCount(predicate, def, ct)` → `.Items` / `.TotalCount` (DATA-0096 removed `DataQueryOptions`).
- **JSON filter DSL**: `Order.Query(filterJson, ct)` takes the JSON DSL (`{"Shipped":{"$in":[true]}}`), **not** SQL/CONTAINS; `Order.QueryRaw(...)` for provider-native.
- **Embeddings**: the class-level `[Embedding]` attribute (`Koan.Data.AI.Attributes`) opts an aggregate into vectorization — there is no per-property `[VectorField]`. Live vector connectors: Weaviate / Qdrant / Milvus.

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — one-screen pillar map
- [Data modeling guide](../../../docs/guides/data-modeling.md) — aggregates, keys, value objects
- [`samples/S1.Web`](../../../samples/S1.Web/README.md) — `Entity<Todo>` CRUD + `[Parent]` relationship navigation
- [DATA-0072 — `[Parent]` relationship attribute](../../../docs/decisions/DATA-0072-parent-relationship-attribute-explicit-type.md)
- [DATA-0077 — Source/Adapter/Partition routing](../../../docs/decisions/DATA-0077-entity-context-source-adapter-partition-routing.md)
- [DATA-0096 — unified filter pipeline (`QueryDefinition`)](../../../docs/decisions/DATA-0096-unified-filter-pipeline.md) · [ARCH-0084 — unified capability model](../../../docs/decisions/ARCH-0084-unified-capability-model.md)
