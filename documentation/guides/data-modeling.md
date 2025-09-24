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
    public Task<Product?> GetProduct() => Product.ById(ProductId);
}
```

### Collection Navigation

```csharp
public class User : Entity<User>
{
    public string Name { get; set; } = "";

    // Collection navigation via query
    public Task<Order[]> GetOrders() =>
        Order.Where(o => o.UserId == Id);

    public Task<Order[]> GetRecentOrders(int days = 30) =>
        Order.Query()
            .Where(o => o.UserId == Id)
            .Where(o => o.Created > DateTimeOffset.UtcNow.AddDays(-days));
}

public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";

    public Task<OrderItem[]> GetItems() =>
        OrderItem.Where(i => i.OrderId == Id);
}
```

## Business Logic on Entities

```csharp
public class Order : Entity<Order>
{
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Business methods
    public async Task AddItem(string productId, int quantity)
    {
        var product = await Product.ById(productId);
        if (product == null) throw new InvalidOperationException("Product not found");

        var item = new OrderItem
        {
            OrderId = Id,
            ProductId = productId,
            Quantity = quantity,
            Price = product.Price
        };

        await item.Save();
        await RecalculateTotal();
    }

    public async Task RecalculateTotal()
    {
        var items = await GetItems();
        Total = items.Sum(i => i.Price * i.Quantity);
        await Save();
    }

    // Static business queries
    public static Task<Order[]> ForUser(string userId) =>
        Query().Where(o => o.UserId == userId);

    public static Task<Order[]> RecentOrders(int days = 7) =>
        Query()
            .Where(o => o.Created > DateTimeOffset.UtcNow.AddDays(-days))
            .OrderByDescending(o => o.Created);
}
```

## Value Objects

```csharp
public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";

    public override string ToString() =>
        $"{Street}, {City}, {State} {ZipCode}, {Country}";
}

public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public Address ShippingAddress { get; set; } = new();
    public Address BillingAddress { get; set; } = new();
}
```

## Enums

```csharp
public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled,
    Returned
}

public enum UserRole
{
    Customer,
    Manager,
    Admin
}

public class Order : Entity<Order>
{
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public bool CanCancel() => Status is OrderStatus.Pending or OrderStatus.Confirmed;
    public bool CanShip() => Status == OrderStatus.Confirmed;
}
```

## Complex Queries

```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public int StockLevel { get; set; }
    public bool IsActive { get; set; } = true;

    // Business queries
    public static Task<Product[]> InStock() =>
        Query().Where(p => p.StockLevel > 0 && p.IsActive);

    public static Task<Product[]> LowStock(int threshold = 10) =>
        Query().Where(p => p.StockLevel <= threshold && p.IsActive);

    public static Task<Product[]> InCategory(string category) =>
        Query().Where(p => p.Category == category && p.IsActive);

    public static Task<Product[]> PriceRange(decimal min, decimal max) =>
        Query().Where(p => p.Price >= min && p.Price <= max && p.IsActive);

    public static Task<Product[]> Search(string query) =>
        Query().Where(p =>
            p.Name.Contains(query) ||
            p.Category.Contains(query))
            .Where(p => p.IsActive);
}
```

## Aggregates and Events

```csharp
public class OrderCreatedEvent
{
    public string OrderId { get; set; } = "";
    public string UserId { get; set; } = "";
    public decimal Total { get; set; }
}

public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public async Task Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be confirmed");

        Status = OrderStatus.Confirmed;
        await Save();

        // Raise domain event
        await new OrderCreatedEvent
        {
            OrderId = Id,
            UserId = UserId,
            Total = Total
        }.Send();
    }
}
```

## Multi-Provider Scenarios

```csharp
// Main data in SQL
public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

// Session data in Redis
[DataAdapter("redis")]
public class UserSession : Entity<UserSession>
{
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