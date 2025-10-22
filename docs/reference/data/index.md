---
type: REF
domain: data
title: "Data Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-07
  status: verified
  scope: docs/reference/data/index.md
---

# Data Pillar Reference

## Contract

- **Inputs**: Familiarity with Koan entities, dependency injection configured via `services.AddKoan()`, and at least one data adapter package (`Koan.Data.Connector.Sqlite`, `Koan.Data.Connector.Postgres`, etc.).
- **Outputs**: Production-ready patterns for modeling entities, enforcing policy, streaming large datasets, and adding semantic/vector capabilities with minimal boilerplate.
- **Error Modes**: Misaligned provider capabilities (e.g., using vector APIs with adapters that do not advertise `vector-search`), lifecycle hooks cancelling mutations, missing configuration values, or long-running streams without pagination.
- **Success Criteria**: Entities handle CRUD, relationships, and business rules through static helpers; streaming and paging guardrails prevent memory pressure; vector workflows and direct SQL escape hatches co-exist without duplicated repositories.

### Edge Cases

- **Provider capability checks** – always read `EntityCaps<T>` (or review adapter documentation) before assuming support for vectors, transactions, or bulk operations.
- **Lifecycle protection** – hooks can cancel writes; surface errors to callers with contextual codes.
- **Streaming cancellation** – pass `CancellationToken` to `AllStream`/`QueryStream` consumers to prevent runaway jobs.
- **Large joins** – prefer raw SQL or CQRS projections when cross-entity materialization would otherwise cause N+1 lookups.
- **Vector storage** – confirm embedding dimensions and provider limits before performing large batch inserts.

---


## Pillar Overview

**Test Coverage:**

- All Data pillar connectors and adapters (Sqlite, Postgres, SqlServer, Mongo, Redis, Json, Couchbase, OpenSearch, Vector) are covered by automated test suites as of 2025-10-07.
- See `tests/Suites/` for per-connector and per-pillar test implementations.

Koan’s Data pillar unifies persistence across SQL, NoSQL, JSON, and vector databases. Every entity is a rich domain object with first-class static helpers (`All`, `Query`, `AllStream`, `FirstPage`, etc.) and lifecycle hooks. Capabilities are discovered automatically from installed adapters.

**Core packages**: `Koan.Data.Core`, adapter-specific packages (for example `Koan.Data.Connector.Postgres`, `Koan.Data.Connector.MongoDB`, `Koan.Data.Connector.Redis`, `Koan.Data.Vector.Redis`).

➤ **Caching**: See the [Koan Cache Reference](./cache.md) for fluent builders, tag invalidation, and policy-driven caching that integrates with `Entity<TEntity>`.

---

## Modeling Quick Start

```csharp
public class Product : Entity<Product>
{
  public string Name { get; set; } = "";
  public decimal Price { get; set; }
  public string Category { get; set; } = "";
  public bool IsActive { get; set; } = true;
}

// Create
var product = await new Product { Name = "Widget", Price = 10.00m }.Save();

// Read
var all = await Product.All();
var widget = await Product.Get(product.Id);

// Update
widget.Price = 15.00m;
await widget.Save();

// Delete
await widget.Delete();
```

All default columns (`Id`, `Created`, `Modified`, etc.) are managed automatically by the adapter.

---

## Static APIs & Business Queries

Centralize domain logic on the entity itself, not in separate repositories.

```csharp
public class Product : Entity<Product>
{
  public static Task<Product[]> InCategory(string category) =>
    Query().Where(p => p.Category == category);

  public static Task<Product[]> Featured() =>
    Query().Where(p => p.IsActive && p.Created > DateTimeOffset.UtcNow.AddDays(-7));
}

var featured = await Product.Featured();
```

Use `Query()` for composable LINQ pipelines, `Where` shortcuts for simple filters, and `FirstPage`/`Page` for cursor-based pagination when returning results to APIs.

---

## Relationships & Navigation

Foreign keys and collection helpers stay on the entity surface.

```csharp
public class Order : Entity<Order>
{
  public string UserId { get; set; } = "";
  public decimal Total { get; set; }

  public Task<User?> GetUser() => User.Get(UserId);

  public static Task<Order[]> ForUser(string userId) =>
    Query().Where(o => o.UserId == userId);
}

public class User : Entity<User>
{
  public string Name { get; set; } = "";

  public Task<Order[]> GetOrders() =>
    Order.Query().Where(o => o.UserId == Id).ToArrayAsync();
}
```

For high-volume navigation, combine `QueryStream` or `FirstPage` with dedicated DTOs to avoid loading entire aggregates at once.

---

## Value Objects & Enums

Value objects embed structure without extra tables.

```csharp
public record Address(string Street, string City, string State, string ZipCode, string Country);

public class Customer : Entity<Customer>
{
  public Address Shipping { get; set; } = new("1 Main", "Seattle", "WA", "98101", "USA");
  public Address Billing { get; set; } = new("1 Main", "Seattle", "WA", "98101", "USA");
}
```

Enums remain serialised as strings by default, keeping queries legible.

```csharp
public enum OrderStatus { Pending, Confirmed, Shipped, Delivered, Cancelled, Returned }

public class Order : Entity<Order>
{
  public OrderStatus Status { get; set; } = OrderStatus.Pending;

  public bool CanCancel() => Status is OrderStatus.Pending or OrderStatus.Confirmed;
  public bool CanShip() => Status == OrderStatus.Confirmed;
}
```

---

## Business Logic & Validation

Encapsulate rules behind intentful methods. Save invariants and side effects live beside the data.

```csharp
public class Order : Entity<Order>
{
  public async Task AddItem(string productId, int quantity)
  {
  var product = await Product.Get(productId)
      ?? throw new InvalidOperationException("Product not found");

    await new OrderItem
    {
      OrderId = Id,
      ProductId = productId,
      Quantity = quantity,
      Price = product.Price
    }.Save();

    await RecalculateTotal();
  }

  public async Task RecalculateTotal()
  {
    var items = await OrderItem.Where(i => i.OrderId == Id);
    Total = items.Sum(i => i.Price * i.Quantity);
    await Save();
  }
}
```

---

## Query Patterns

Compose queries with familiar LINQ operators. Koan pushes down expressions whenever the adapter supports them and falls back to in-memory evaluation otherwise.

```csharp
public static Task<Product[]> LowStock(int threshold = 10) =>
  Query()
    .Where(p => p.StockLevel < threshold && p.IsActive)
    .OrderByDescending(p => p.StockLevel)
    .ToArrayAsync();
```

For analytical workloads or reporting, prefer `FirstPage`/`Page` to maintain cursor-based pagination:

```csharp
var firstPage = await Product.FirstPage(pageSize: 50, orderBy: p => p.Created);
var nextPage = await firstPage.NextPage();
```

---

## Streaming & Background Workloads

Stream massive result sets without materializing everything into memory.

```csharp
await foreach (var product in Product.AllStream(ct))
{
  await product.SyncToSearchIndex();
}
```

Combine streaming with semantic pipelines (see the Flow pillar) when orchestrating AI-augmented enrichment.

---

## Vector Search & AI Integration

Add embeddings directly to entities for semantic retrieval.

```csharp
public class Document : Entity<Document>
{
  public string Title { get; set; } = "";
  public string Content { get; set; } = "";

  [VectorField]
  public float[] ContentEmbedding { get; set; } = [];

  public static Task<Document[]> SimilarTo(string query) =>
    Vector<Document>.SearchAsync(query, limit: 20);
}
```

Pair with the AI pillar to generate embeddings inside pipelines or background services.

---

## Lifecycle Events & Policy Enforcement

Lifecycle hooks wrap every mutation, enabling policy gates, enrichment, and telemetry.

```csharp
public static class ProductLifecycle
{
  static ProductLifecycle()
  {
    Product.Events
      .Setup(ctx =>
      {
        ctx.ProtectAll();
        ctx.AllowMutation(nameof(Product.Price));
      })
      .BeforeUpsert(ctx =>
      {
        if (ctx.Current.Price < 0)
        {
          return ctx.Cancel("Price must be non-negative.", "product.price_negative");
        }

        return ctx.Proceed();
      });
  }
}
```

See the dedicated [Entity Lifecycle Events reference](./entity-lifecycle-events.md) for the full hook matrix, cancellation semantics, and batching guidance.

---

## Direct SQL & Escape Hatches

Drop to raw SQL or command APIs when you need custom joins, projections, or adapter-specific features.

```csharp
var results = await Data<Product>.Query(@"
  SELECT p.*, c.Name AS CategoryName
  FROM Products p
  JOIN Categories c ON p.CategoryId = c.Id
  WHERE p.Price > @minPrice",
  new { minPrice = 100 });
```

All direct commands respect configured connections, logging, and retry policies.

---

## Provider Matrix

| Provider   | Package                         | Primary Use Case                        |
| ---------- | ------------------------------- | --------------------------------------- |
| SQLite     | `Koan.Data.Connector.Sqlite`    | Local development, embedded deployments |
| Postgres   | `Koan.Data.Connector.Postgres`  | Production relational workloads         |
| SQL Server | `Koan.Data.Connector.SqlServer` | Legacy and enterprise relational        |
| MongoDB    | `Koan.Data.Connector.MongoDB`   | Document storage                        |
| Redis      | `Koan.Data.Connector.Redis`     | Caching, vector search                  |
| JSON       | `Koan.Data.Connector.Json`      | File-based storage                      |

Consult each adapter’s README for capability flags (bulk operations, vectors, transactions, etc.).

---

## Configuration & Environment

```json
{
  "Koan": {
    "Data": {
      "DefaultProvider": "Sqlite",
      "Sqlite": {
        "ConnectionString": "Data Source=app.db"
      },
      "Postgres": {
        "ConnectionString": "Host=localhost;Database=myapp"
      }
    }
  }
}
```

Environment variables override the same hierarchy:

```bash
export Koan__Data__DefaultProvider=Postgres
export Koan__Data__Postgres__ConnectionString="Host=prod;Database=app"
```

---

## Related Reading

- [Entity Lifecycle Events](./entity-lifecycle-events.md)
- [Flow Pillar Reference](../flow/index.md) for ingestion pipelines and semantic augmentation
- [AI Pillar Reference](../ai/index.md) for embedding generation and retrieval-augmented generation
