---
type: REF
domain: data
title: "Data Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/reference/data/index.md
---

# Data Pillar Reference

**Document Type**: REF
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-09-28
**Framework Version**: v0.6.2

---

## Overview

Unified data access across SQL, NoSQL, JSON, and vector databases.

**Packages**: `Koan.Data.*`

## Entity Patterns

```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

// Create
var product = await new Product { Name = "Widget", Price = 10.00m }.Save();

// Read
var products = await Product.All();
var widget = await Product.ById("some-id");

// Query
var expensive = await Product.Where(p => p.Price > 100);
var electronics = await Product.Query()
    .Where(p => p.Category == "Electronics")
    .OrderBy(p => p.Name);

// Update
product.Price = 15.00m;
await product.Save();

// Delete
await product.Delete();
```

## Static Methods

Business logic lives on entities:

```csharp
public class Product : Entity<Product>
{
    public static Task<Product[]> InCategory(string category) =>
        Query().Where(p => p.Category == category);

    public static Task<Product[]> OnSale() =>
        Query().Where(p => p.Price < p.OriginalPrice);
}

// Usage
var electronics = await Product.InCategory("Electronics");
var deals = await Product.OnSale();
```

## Lifecycle Events

Lifecycle pipelines let entities guard mutations, enforce policy, and enrich loads without external repositories. Configure hooks with `TEntity.Events` during startup.

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

See the [Entity Lifecycle Events reference](./entity-lifecycle-events.md) for stage order, cancellation patterns, atomic batching, and reset guidance.

## Relationships

```csharp
public class User : Entity<User>
{
    public string Name { get; set; } = "";
}

public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";
    public decimal Total { get; set; }

    // Navigation
    public Task<User?> GetUser() => User.ById(UserId);

    // Queries
    public static Task<Order[]> ForUser(string userId) =>
        Query().Where(o => o.UserId == userId);
}
```

## Vector Search

```csharp
public class Document : Entity<Document>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";

    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];

    // Semantic search
    public static Task<Document[]> SimilarTo(string query) =>
        Vector<Document>.SearchAsync(query);
}

// Usage
var similar = await Document.SimilarTo("machine learning");
```

## Streaming

Large datasets without memory pressure:

```csharp
await foreach (var product in Product.AllStream())
{
    await ProcessProduct(product);
}
```

## Direct SQL

When you need custom queries:

```csharp
var results = await Data<Product>.Query(@"
    SELECT p.*, c.Name as CategoryName
    FROM Products p
    JOIN Categories c ON p.CategoryId = c.Id
    WHERE p.Price > @minPrice",
    new { minPrice = 100 });
```

## Providers

| Provider | Package              | Use Case                    |
| -------- | -------------------- | --------------------------- |
| SQLite   | `Koan.Data.Sqlite`   | Local development, embedded |
| Postgres | `Koan.Data.Postgres` | Production relational       |
| MongoDB  | `Koan.Data.MongoDB`  | Document storage            |
| Redis    | `Koan.Data.Redis`    | Caching, vector search      |
| JSON     | `Koan.Data.Json`     | File-based storage          |

## Configuration

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

Environment variables:

```bash
export Koan__Data__DefaultProvider=Postgres
export Koan__Data__Postgres__ConnectionString="Host=prod;Database=app"
```

## Multi-Provider

Same code, different storage:

```csharp
// Uses configured default provider
var products = await Product.All();

// Provider-specific when needed
[DataAdapter("redis")]
public class CachedData : Entity<CachedData> { }
```

Capability detection handles provider differences automatically.

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+
