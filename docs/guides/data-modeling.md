---
type: GUIDE
domain: data
title: "Data Modeling with Koan"
audience: [developers, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Data Modeling with Koan

**Document Type**: GUIDE
**Target Audience**: Developers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Basic Entities

```csharp
public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastLogin { get; set; }
}
```

IDs and timestamps (`Created`, `Modified`) are automatic.

## Relationships

### Foreign Keys

```csharp
public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Navigation method
    public Task<User?> GetUser() => User.ById(UserId);
}

public class OrderItem : Entity<OrderItem>
{
    public string OrderId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }

    // Navigation methods
    public Task<Order?> GetOrder() => Order.ById(OrderId);
    type: GUIDE
    domain: data
    title: "Data Modeling Playbook"
    audience: [developers, architects, ai-agents]
    last_updated: 2025-09-28
    framework_version: v0.6.2
    status: current
    validation:
      date_last_tested: 2025-09-28
      status: verified
      scope: docs/guides/data-modeling.md
{
 
    # Data Modeling Playbook

    ## Contract

    - **Inputs**: A Koan application with `services.AddKoan()` registered, at least one data adapter, and baseline understanding of entities and dependency injection.
    - **Outputs**: An entity-first domain model with encapsulated business rules, safe relationships, and the right streaming/vector patterns for downstream systems.
    - **Error Modes**: Skipping capability checks (vector, bulk, transactions), missing lifecycle whitelists when `ProtectAll()` is active, or materializing large result sets in interactive endpoints without paging.
    - **Success Criteria**: Entities expose clear static helpers, business invariants live with the model, related pillars (Flow, AI, Messaging) plug in without additional repositories.

    ### Edge Cases

    - **Multiple adapters** – choose defaults via configuration and scope specialty entities with `[DataAdapter]` attributes.
    - **Lifecycle gatekeeping** – treat cancellations as first-class outcomes; convert to domain-specific error messages.
    - **Background workloads** – always pass `CancellationToken` down to `AllStream`/`QueryStream` calls.
    - **Vector migrations** – align embedding dimensions with the configured provider before backfilling historical data.

    ---

    ## How to Use This Playbook

    Each section links to the canonical reference for deeper samples. Treat this guide as a scenario checklist while modelling new aggregates.

    - 📌 **Reference hub**: [Data Pillar Reference](../reference/data/index.md)
    - ⚙️ **Lifecycle matrix**: [Entity Lifecycle Events](../reference/data/entity-lifecycle-events.md)
    - 🔄 **Flow integration**: [Flow Pillar Reference](../reference/flow/index.md)

    ---

    ## 1. Define the Aggregate Boundary

    1. Sketch the entity’s core properties; keep identifiers, timestamps, and soft-delete flags implicit—they are supplied by Koan.
    2. Add default values to avoid `null` checks in controllers.
    3. Record quick-start CRUD helpers with the static `Query()`/`Where()` APIs.

    ```csharp
    public class Todo : Entity<Todo>
    {
        public string Title { get; set; } = "";
        public bool IsCompleted { get; set; }

        public static Task<Todo[]> Recent(int days = 7) =>
            Query().Where(t => t.Created > DateTimeOffset.UtcNow.AddDays(-days));
    }
    ```

    🔎 Deep dive: [Modeling quick start](../reference/data/index.md#modeling-quick-start)

    ---

    ## 2. Capture Relationships Early

    - Represent foreign keys as `string` identifiers to stay compatible with multi-provider setups.
    - Provide navigation helpers on both sides so higher layers never reach into repositories.
    - Use LINQ-based static methods for common lookups instead of injecting bespoke services.

    ```csharp
    public class Order : Entity<Order>
    {
        public string UserId { get; set; } = "";
        public Task<User?> GetUser() => User.ById(UserId);
    }

    public class User : Entity<User>
    {
        public Task<Order[]> GetOrders() =>
            Order.Query().Where(o => o.UserId == Id).ToArrayAsync();
    }
    ```

    🔎 Deep dive: [Relationships & navigation](../reference/data/index.md#relationships--navigation)

    ---

    ## 3. Enrich with Value Objects and Enums

    - Wrap cohesive fields (addresses, money, dimensions) inside records or classes to keep invariants local.
    - Prefer enums for small classification sets; Koan stores them as strings unless overridden.
    - Document optional vs required data through constructors or property defaults.

    🔎 Deep dive: [Value objects & enums](../reference/data/index.md#value-objects--enums)

    ---

    ## 4. Encapsulate Business Logic on the Entity

    - Expose imperative methods (`Ship`, `Cancel`, `ApplyDiscount`) rather than mutating properties externally.
    - Chain async operations freely—entities are regular C# classes.
    - Use static helpers for query-based workflows (reporting, dashboards, background jobs).

    🔎 Deep dive: [Business logic & validation](../reference/data/index.md#business-logic--validation)

    ---

    ## 5. Apply Lifecycle Policies

    - Start every lifecycle module with `ProtectAll()` and opt-in only the properties you intend to mutate.
    - `BeforeUpsert`/`BeforeDelete` are ideal for guardrails; `AfterLoad` can hydrate derived data before it reaches controllers.
    - Keep hook classes static to avoid double registration when DI scopes rebuild.

    🔎 Deep dive: [Lifecycle events & policy enforcement](../reference/data/index.md#lifecycle-events--policy-enforcement)

    ---

    ## 6. Design for Scale from Day One

    - Decide whether high-volume surfaces should use `FirstPage`, `Page`, or streaming APIs; expose them via controllers accordingly.
    - For AI or analytics, annotate vector fields and plan enrichment flows with Flow pipelines.
    - Document escape hatches (direct SQL, projection DTOs) so the team knows when to drop down.

    🔎 Deep dive:
      - [Streaming & background workloads](../reference/data/index.md#streaming--background-workloads)
      - [Vector search & AI integration](../reference/data/index.md#vector-search--ai-integration)
      - [Direct SQL & escape hatches](../reference/data/index.md#direct-sql--escape-hatches)

    ---

    ## 7. Wire Configuration and Capabilities

    - Pick a default provider in configuration and override per entity only when required.
    - Review capability metadata (`EntityCaps<T>`) when enabling features such as transactions, vectors, or sharding.
    - Capture environment overrides for non-production infrastructure in `launchSettings.json` or deployment manifests.

    🔎 Deep dive: [Configuration & environment](../reference/data/index.md#configuration--environment)

    ---

    ## 8. Validate the Aggregate

    Use this quick checklist before exposing the model to other pillars:

    - [ ] Static helpers cover expected CRUD and query shapes.
    - [ ] Lifecycle hooks guard critical invariants and emit meaningful error codes.
    - [ ] Relationships expose bidirectional helpers (or explicit query methods).
    - [ ] Streaming/paging APIs exist for collection endpoints.
    - [ ] Downstream systems (Flow, Messaging, AI) know which helpers to call.

    ---

    ## Next Steps

    - Extend the model with [Flow ingestion pipelines](../reference/flow/index.md#semantic-pipelines) for enrichment jobs.
    - Add messaging events via `Entity<T>.Send()` to broadcast domain changes.
    - Layer payload transformers on top of your controllers—see the [Web pillar reference](../reference/web/index.md#payload-transformers).
    public string UserId { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

// Search data with vectors
[DataAdapter("weaviate")]
public class ProductSearch : Entity<ProductSearch>
{
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    [VectorField]
    public float[] DescriptionEmbedding { get; set; } = [];

    public static Task<ProductSearch[]> SimilarTo(string query) =>
        Vector<ProductSearch>.SearchAsync(query);
}
```

## Validation and Constraints

```csharp
public class User : Entity<User>
{
    private string _email = "";

    public string Name { get; set; } = "";

    public string Email
    {
        get => _email;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Email is required");

            if (!value.Contains("@"))
                throw new ArgumentException("Invalid email format");

            _email = value.ToLowerInvariant();
        }
    }

    public async Task<bool> IsEmailUnique()
    {
        var existing = await Query().Where(u => u.Email == Email && u.Id != Id);
        return !existing.Any();
    }

    public override async Task<User> Save()
    {
        if (!await IsEmailUnique())
            throw new InvalidOperationException("Email already exists");

        return await base.Save();
    }
}
```

## Soft Deletes

```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; } = false;
    public DateTimeOffset? DeletedAt { get; set; }

    public async Task SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        await Save();
    }

    // Filter out deleted items by default
    public static Task<Product[]> Active() =>
        Query().Where(p => !p.IsDeleted);

    public static Task<Product[]> All() =>
        Query(); // Includes deleted items
}
```

## Audit Trails

```csharp
public class AuditLog : Entity<AuditLog>
{
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string Action { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Changes { get; set; } = "";
}

public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
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
            Changes = JsonSerializer.Serialize(this)
        }.Save();

        return result;
    }
}
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+