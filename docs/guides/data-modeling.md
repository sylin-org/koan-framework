---
type: GUIDE
domain: data
title: "Data Modeling Playbook"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-11-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-09
  status: verified
  scope: all-examples-tested
related_guides:
  - entity-capabilities-howto.md
  - canon-capabilities-howto.md
  - ai-vector-howto.md
  - performance.md
---

# Data Modeling Playbook

## Contract

- **Inputs**: A Koan application with `builder.Services.AddKoan()`, at least one data adapter, and baseline knowledge of entities and dependency injection.
- **Outputs**: An entity-first domain model with encapsulated rules, safe relationships, and streaming/vector patterns ready for downstream systems.
- **Error Modes**: Capability mismatches, missing lifecycle opt-ins when `ProtectAll()` is active, or unbounded materialization in interactive endpoints.
- **Success Criteria**: Entities expose clear static helpers, business invariants live with the model, and Flow/AI/Messaging integrations avoid custom repositories.

### Edge Cases

- **Multiple adapters** – set defaults via configuration and scope specialty stores with `[DataAdapter]`.
- **Lifecycle gatekeeping** – treat cancellations as first-class outcomes and convert them into domain-specific errors.
- **Background workloads** – always pass `CancellationToken` through `AllStream`/`QueryStream` calls.
- **Vector migrations** – align embedding dimensions with the configured provider before backfilling historical data.
- **Bulk writes** – verify adapter capabilities before assuming transactional or batched semantics.

---

## How to Use This Playbook

- 📌 Reference hub: [Data Pillar Reference](../reference/data/index.md)
- 🔁 Lifecycle matrix: [Entity Lifecycle Events](../reference/data/entity-lifecycle-events.md)
- 🌊 Orchestration partner: [Flow Pillar Reference](../reference/flow/index.md)
- 📮 Delivery surface: [API Delivery Playbook](./building-apis.md)

Treat each section as a readiness checklist before shipping a model.

---

## 1. Define the Aggregate Boundary

1. Model required fields first; Koan supplies identifiers automatically. For created/updated timestamps, declare `DateTimeOffset` properties marked `[Timestamp]` (creation stamp) or `[Timestamp(OnSave = true)]` (updated on every save).
2. Add defaults to avoid null checks inside controllers.
3. Publish starter query helpers on the entity itself.

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }

    [Timestamp]
    public DateTimeOffset Created { get; set; }

    public static Task<IReadOnlyList<Todo>> Recent(int days = 7, CancellationToken ct = default) =>
        Query(t => t.Created > DateTimeOffset.UtcNow.AddDays(-days), ct);
}
```

---

## 2. Capture Relationships Early

- Store foreign keys as strings for provider neutrality.
- Offer navigation helpers on both sides so higher layers never reach for repositories.
- Wrap common lookups inside static methods.

```csharp
public class Order : Entity<Order>
{
    [Parent(typeof(User))]
    public string UserId { get; set; } = "";

    [Timestamp]
    public DateTimeOffset Created { get; set; }

    public Task<User?> GetUser(CancellationToken ct = default) => User.Get(UserId, ct);
}

public class User : Entity<User>
{
    public Task<IReadOnlyList<Order>> GetRecentOrders(CancellationToken ct = default) =>
        Order.Query(o => o.UserId == Id && o.Created > DateTimeOffset.UtcNow.AddDays(-30), ct);
}
```

---

## 3. Enrich with Value Objects and Enums

- Wrap cohesive data (addresses, money) inside records to centralize validation.
- Prefer enums or discriminated unions for classification.
- Document optional data through constructors or property defaults.

```csharp
public record Money(decimal Amount, string Currency);

public class Invoice : Entity<Invoice>
{
    public Money Total { get; set; } = new(0m, "USD");
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public void MarkSent() => Status = InvoiceStatus.Sent;
}
```

---

## 4. Encapsulate Business Logic

- Express behaviors as methods on the entity, not in controllers.
- Chain async operations freely—entities are plain C# classes.
- Emit domain-specific errors for consumers.

```csharp
public class Product : Entity<Product>
{
    public decimal Price { get; private set; }

    public Task<Product> ApplyDiscount(decimal amount)
    {
        if (amount <= 0 || amount >= Price)
            throw new InvalidOperationException("Discount must be less than current price.");

        Price -= amount;
        return Save();
    }
}
```

---

## 5. Apply Lifecycle Policies

- Register hooks at startup against the entity's static `Events` facade.
- Use a `Setup` handler to `ProtectAll()` and opt-in only the properties you intend to mutate via `AllowMutation(...)`.
- Use `BeforeUpsert`/`BeforeRemove` for guardrails (return `ctx.Proceed()` or `ctx.Cancel(...)`), `AfterLoad` for hydration.

```csharp
public static class ProductLifecycle
{
    public static void Configure()
    {
        Product.Events.Setup(ctx =>
        {
            ctx.ProtectAll();
            ctx.AllowMutation(nameof(Product.Price));
            ctx.AllowMutation(nameof(Product.Description));
        });

        Product.Events.BeforeUpsert(ctx =>
            ctx.Current.Price < 0
                ? ctx.Cancel("Price cannot be negative.", "product.negative_price")
                : ctx.Proceed());
    }
}
```

---

## 6. Design for Scale from Day One

- Prefer `FirstPage`/`Page` or streaming helpers for high-volume reads.
- Document batch sizes for Flow pipelines and background jobs.
- Declare vector annotations early if semantic search or AI is on the roadmap.

---

## 7. Wire Configuration and Capabilities

- Declare the default adapter in configuration and scope overrides with `[DataAdapter("alias")]`.
- Inspect `Data<T, string>.Capabilities` (a `CapabilitySet`) before enabling transactions, vectors, or sharding.
- Capture environment-specific overrides in deployment manifests or `launchSettings.json`.

---

## 8. Validate the Aggregate

Use this checklist before exposing the model:

- [ ] Static helpers cover expected CRUD and query shapes.
- [ ] Lifecycle hooks guard critical invariants and emit meaningful error codes.
- [ ] Relationships expose navigation helpers or dedicated query methods.
- [ ] Paging/streaming helpers support high-volume reads.
- [ ] Downstream pillars (Flow, Messaging, AI) know which helpers to call.

---

## Pattern Recipes

Leverage these recipes as living examples. Full walkthroughs live in the [Entity Pattern Recipe Catalog](../examples/entity-pattern-recipes.md).

### Stage 1 – CRUD Backbone

- Minimal entity definition with default constructor values.
- Controllers delegate persistence to `Save()` and fetch with `Get()` or custom statics.

```csharp
public class InventoryItem : Entity<InventoryItem>
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }

    public static async Task<InventoryItem?> BySku(string sku, CancellationToken ct = default) =>
        (await Query(i => i.Sku == sku, ct)).FirstOrDefault();
}
```

### Stage 2 – Event-Driven Messaging

- Pair domain updates with events using the same entity patterns.
- Flow handlers subscribe to create/update hooks to broadcast changes.

```csharp
public class PriceChanged : Entity<PriceChanged>
{
    public string ProductId { get; set; } = "";
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
}

public static class ProductEvents
{
    public static void Configure(FlowPipeline pipeline) =>
        pipeline.OnUpdate<Product>(async (updated, previous) =>
        {
            if (updated.Price == previous.Price) return UpdateResult.Continue();

            await new PriceChanged
            {
                ProductId = updated.Id,
                OldPrice = previous.Price,
                NewPrice = updated.Price
            }.Send();

            return UpdateResult.Continue();
        });
}
```

### Stage 3 – AI-Enriched Domain

- Store embeddings alongside domain data.
- Generate vectors during writes or in Flow background jobs.

```csharp
[DataAdapter("weaviate")]
[Embedding(Template = "{Description}")]
public class ProductSearch : Entity<ProductSearch>
{
    public string ProductId { get; set; } = "";
    public string Description { get; set; } = "";

    public float[] DescriptionEmbedding { get; set; } = [];

    public static Task<VectorQueryResult<string>> SimilarTo(float[] queryVector, CancellationToken ct = default) =>
        Vector<ProductSearch>.Search(queryVector, ct: ct);
}
```

### Extended Moves

- Soft deletes with opt-in query helpers.
- Audit trails captured via secondary entities.
- Relationship fan-out for projections and dashboards.

Each pattern below shows the canonical implementation.

---

## Advanced Patterns

### Soft Delete with Guardrails

```csharp
public class KnowledgeArticle : Entity<KnowledgeArticle>
{
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public async Task SoftDelete()
    {
        if (IsDeleted) return;

        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        await Save();
    }

    public static Task<IReadOnlyList<KnowledgeArticle>> Active(CancellationToken ct = default) =>
        Query(a => !a.IsDeleted, ct);
}
```

### Audit Trail Capture

```csharp
public class AuditLog : Entity<AuditLog>
{
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string Action { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Product : Entity<Product>
{
    public decimal Price { get; set; }

    public override async Task<Product> Save()
    {
        var isNew = string.IsNullOrEmpty(Id);
        var result = await base.Save();

        await new AuditLog
        {
            EntityId = result.Id,
            EntityType = nameof(Product),
            Action = isNew ? "Created" : "Updated",
            Snapshot = JsonSerializer.Serialize(result)
        }.Save();

        return result;
    }
}
```

---

## Next Steps

- Automate enrichment with the [AI & Vector How-To](./ai-vector-howto.md).
- Publish APIs with the [API Delivery Playbook](./building-apis.md).
- Expose diagnostics and runbooks inside the [Koan Troubleshooting Hub](../support/troubleshooting.md).

---

## Validation

- Last reviewed: 2025-09-28
- Verified against Koan v0.6.2 sample services.
