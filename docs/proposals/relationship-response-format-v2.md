# Instance-Based Relationship Response Format (v2)

## Decision: Selective Enrichment with Child Class Grouping

### Motivation
- **Selective Enrichment**: Only requested entities receive RelationshipGraph format
- **Raw Relationships**: Parents and children remain as raw entities (no recursive enrichment)
- **Child Class Grouping**: Clear organization by child class name and reference property
- **Instance-Based Loading**: Leverages semantic instance methods for intuitive relationship navigation

### Core Principles

1. **Only Requested Entities Enriched**: Using `?with=all` or `.GetRelatives()` triggers enrichment
2. **Raw Parent/Child Data**: No recursive enrichment - parents/children stay in original format
3. **Semantic Validation**: Backend validates relationship cardinality before loading
4. **Performance Optimized**: Batch loading and streaming support for large datasets

### Response Structure

```json
{
  "entity": { /* enriched entity properties */ },
  "parents": {
    "CustomerId": { /* raw customer object */ },
    "CategoryId": { /* raw category object */ }
  },
  "children": {
    "OrderItem": {
      "OrderId": [ /* raw order item objects */ ]
    },
    "Review": {
      "OrderId": [ /* raw review objects */ ]
    }
  }
}
```

**Key Structure Elements**:
- `entity`: The requested/enriched entity
- `parents`: Dictionary where key = property name, value = raw parent entity
- `children`: Dictionary where key = child class name, value = dictionary of reference properties to arrays

### Enhanced DTO with Child Class Grouping

```csharp
public class RelationshipGraph<TEntity>
{
    /// <summary>
    /// The enriched entity being requested
    /// </summary>
    public TEntity Entity { get; set; }

    /// <summary>
    /// Raw parent entities (no enrichment). Key = property name, Value = raw parent entity
    /// </summary>
    public Dictionary<string, object?> Parents { get; set; } = new();

    /// <summary>
    /// Raw child entities (no enrichment). Structure: ChildClassName -> ReferenceProperty -> Raw entities[]
    /// </summary>
    public Dictionary<string, Dictionary<string, IReadOnlyList<object>>> Children { get; set; } = new();
}
```

### API Usage Examples

#### Full Enrichment
```http
GET /api/orders/123?with=all
```

```json
{
  "entity": {
    "id": "123",
    "customerId": "456",
    "categoryId": "789",
    "total": 299.99,
    "status": "shipped"
  },
  "parents": {
    "CustomerId": {"id": "456", "name": "John Doe", "email": "john@example.com"},
    "CategoryId": {"id": "789", "name": "Electronics"}
  },
  "children": {
    "OrderItem": {
      "OrderId": [
        {"id": "item-1", "orderId": "123", "productId": "prod-1", "quantity": 2},
        {"id": "item-2", "orderId": "123", "productId": "prod-2", "quantity": 1}
      ]
    },
    "Review": {
      "OrderId": [
        {"id": "review-1", "orderId": "123", "rating": 5, "comment": "Great!"}
      ]
    }
  }
}
```

### Instance Method Usage Patterns

#### Semantic Single Parent/Child (Validated)
```csharp
// ✅ Works for single-parent models
var orderItem = await Data<OrderItem, string>.GetAsync("item-123");
var order = await orderItem.GetParent<Order>();          // Typed
var parent = await orderItem.GetParent();               // Object

// ❌ Throws for multi-parent models
var order = await Data<Order, string>.GetAsync("order-456");
var parent = await order.GetParent();
// InvalidOperationException: "Order has multiple parents. Use GetParents() instead"
```

#### Explicit Multi-Relationship Methods
```csharp
var order = await Data<Order, string>.GetAsync("order-456");
var customer = await order.GetParent<Customer>();               // Typed single
var allParents = await order.GetParents();                     // All parents
var items = await order.GetChildren<OrderItem>();              // Typed children
var specificItems = await order.GetChildren<OrderItem>("OrderId");  // By property
```

#### Streaming and Batch Operations
```csharp
// ✅ Clean streaming syntax
await foreach (var enriched in Data<Order, string>.AllStream().Relatives())
{
    Console.WriteLine($"Order: {enriched.Entity.Id}");
    Console.WriteLine($"Customer: {enriched.Parents["CustomerId"]}");
}

// ✅ Batch collection enrichment
var orders = new List<Order> { order1, order2, order3 };
var enrichedOrders = await orders.Relatives();
```

### Benefits

- **Selective Enrichment**: Only requested entities get RelationshipGraph format
- **Raw Relationships**: Parents/children remain as original entities (no recursive enrichment)
- **Semantic Validation**: Backend validates cardinality with clear error messages
- **Child Class Organization**: Clear grouping by class name and reference property
- **Performance Optimized**: Batch loading prevents N+1 queries
- **Streaming Support**: High-performance processing of large datasets

### Migration from v1

#### Before (v1 format)
```json
{
  "id": "123",
  "customerId": "456",
  "_parent": {
    "CustomerId": {"id": "456", "name": "John"}
  }
}
```

#### After (v2 format)
```json
{
  "entity": {"id": "123", "customerId": "456"},
  "parents": {
    "CustomerId": {"id": "456", "name": "John"}
  },
  "children": {
    "OrderItem": {
      "OrderId": [/* items */]
    }
  }
}
```

### Implementation Notes

- **Breaking Change**: Direct replacement of `_parent` format
- **API Parameter**: Use `?with=all` instead of `?with=relatives`
- **Instance Methods**: All relationship navigation through Entity instance methods
- **No Backward Bridge**: Clean migration without compatibility layer

This format provides clean separation, semantic validation, and high-performance relationship loading while maintaining simplicity and predictable response structures.
