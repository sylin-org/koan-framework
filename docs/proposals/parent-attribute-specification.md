# ParentAttribute Specification
**Enhanced Parent Relationship Attribute Design**

## Attribute Definition

```csharp
using System;

namespace Sora.Data.Core.Relationships;

/// <summary>
/// Marks a property as a reference to a parent entity.
/// Provides clear, semantic parent-child relationship declaration.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ParentAttribute : Attribute
{
    public Type EntityType { get; }
    public string? Role { get; }
    public string? PayloadPath { get; }
    
    /// <summary>
    /// Primary constructor for simple parent relationships.
    /// </summary>
    /// <param name="entityType">The parent entity type</param>
    public ParentAttribute(Type entityType)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    }
    
    /// <summary>
    /// Full constructor for advanced parent relationship scenarios.
    /// </summary>
    /// <param name="entityType">The parent entity type</param>
    /// <param name="role">Optional role name for disambiguation</param>
    /// <param name="payloadPath">Optional path in payload for parent key extraction</param>
    public ParentAttribute(Type entityType, string? role = null, string? payloadPath = null) 
        : this(entityType)
    {
        Role = string.IsNullOrWhiteSpace(role) ? null : role;
        PayloadPath = string.IsNullOrWhiteSpace(payloadPath) ? null : payloadPath;
    }
}
```

## Usage Examples

### Basic Parent Relationship
```csharp
public class Product : Entity<Product, string>
{
    [Parent(typeof(Category))]
    public string CategoryId { get; set; }
}
```

### Multiple Parents
```csharp
public class Order : Entity<Order, string>
{
    [Parent(typeof(Customer))]
    public string CustomerId { get; set; }
    
    [Parent(typeof(Category))]
    public string CategoryId { get; set; }
}
```

### With Explicit EntityType Parameter
```csharp
public class OrderItem : Entity<OrderItem, string>
{
    [Parent(EntityType = typeof(Order))]
    public string OrderId { get; set; }
    
    [Parent(EntityType = typeof(Product))]
    public string ProductId { get; set; }
}
```

### With Role Disambiguation
```csharp
public class Transaction : Entity<Transaction, string>
{
    [Parent(typeof(Account), Role = "Source")]
    public string SourceAccountId { get; set; }
    
    [Parent(typeof(Account), Role = "Destination")]
    public string DestinationAccountId { get; set; }
}
```

### With PayloadPath for Complex Extraction
```csharp
public class Reading : FlowValueObject<Reading>
{
    [Parent(typeof(Sensor), PayloadPath = "device.sensor.id")]
    public string SensorId { get; set; }
}
```

### Self-Referencing Entities
```csharp
public class Category : Entity<Category>
{
    [Parent(typeof(Category))]  // Self-reference for hierarchy
    public string? ParentCategoryId { get; set; }
    
    public string Name { get; set; } = default!;
}

// Usage examples
var electronics = await Category.Get("electronics-id");
var subcategories = await electronics.GetChildren<Category>();

// Multiple relationship methods
var allParents = await electronics.GetParents();
// Returns: Dictionary<string, object> { "Category": parentCategory }

var allDescendants = await electronics.GetDescendants();
// Returns: Dictionary<string, List<object>>
// { "Category": [subcategory1, subcategory2, ...], "Product": [product1, product2, ...] }

var allRelatives = await electronics.GetRelatives();
// Returns: EntityRelatives { Parents: {...}, Descendants: {...} }

var pathToRoot = await laptopCategory.GetHierarchyPath();
```

## Migration from ParentKeyAttribute

### Before (Flow-specific)
```csharp
[ParentKey(parent: typeof(Sensor))]
public string SensorId { get; set; }

[ParentKey(parent: typeof(Device), payloadPath: "DeviceIdentifier")]
public string DeviceId { get; set; }
```

### After (Data-layer universal)
```csharp
[Parent(typeof(Sensor))]
public string SensorId { get; set; }

[Parent(typeof(Device), payloadPath: "DeviceIdentifier")]
public string DeviceId { get; set; }
```

## Benefits of ParentAttribute Design

### 1. **Semantic Clarity**
- ✅ **Obvious Intent**: `[Parent(typeof(Customer))]` immediately conveys parent relationship
- ✅ **Clear Naming**: No confusion about what the attribute does
- ✅ **Self-Documenting**: Code reads naturally

### 2. **Flexible Usage Patterns**
```csharp
// Concise for simple cases
[Parent(typeof(Customer))]

// Explicit for clarity
[Parent(EntityType = typeof(Customer))]

// Full configuration
[Parent(typeof(Account), Role = "Source", PayloadPath = "sourceAccount.id")]
```

### 3. **Multiple Constructor Support**
- **Simple Constructor**: `Parent(Type entityType)` for common cases
- **Full Constructor**: Additional parameters for complex scenarios
- **Named Parameters**: Support both positional and named parameter styles

### 4. **Backward Compatibility**
- **Same Parameters**: EntityType, Role, PayloadPath match ParentKey semantics
- **Easy Migration**: Simple find-and-replace from ParentKey to Parent
- **Preserved Functionality**: All existing parent resolution logic continues working

### 5. **Framework Integration**
```csharp
// Works with relationship metadata service
var parentInfo = relationshipMetadata.GetParentRelationship(typeof(Order));
// Returns: (EntityType: typeof(Customer), ForeignKeyProperty: "CustomerId")

// Works with Entity base class methods (clean API without Async suffix)
var customer = await order.GetParent<Customer>();
var orders = await customer.GetChildren<Order>();

// Self-referencing hierarchy navigation
var subcategories = await category.GetChildren<Category>();
var allParents = await category.GetParents();  // Dictionary<string, object>
var allDescendants = await category.GetDescendants();  // Dictionary<string, List<object>>
var allRelatives = await category.GetRelatives();  // EntityRelatives
var pathFromRoot = await category.GetHierarchyPath();  // Path to root
```

## Entity Navigation Methods

The following methods are available on all Entity<T> base classes:

- **`GetParent<TParent>()`**: Navigate to parent entity
- **`GetChildren<TChild>()`**: Get all child entities
- **`GetParents()`**: Get all parent entities grouped by type
- **`GetDescendants(maxDepth = 10)`**: Get complete subtree grouped by entity type
- **`GetRelatives(maxDepth = 10)`**: Get both parents and descendants in a single call
- **`GetHierarchyPath()`**: Get path from root to current entity (self-referencing)

```

## API Response Integration

### Query Parameter Support
```http
# Load parent relationships by entity type name
GET /api/orders/123?with=customer,category
```

### Response Structure
```json
{
  "id": "123",
  "customerId": "456",
  "categoryId": "c01",
  "_parent": {
    "customer": {"id": "456", "name": "John Doe"},
    "category": {"id": "c01", "name": "Electronics"}
  }
}
```

### Relationship Discovery
```csharp
// Framework can discover parent relationships
var relationships = relationshipMetadata.GetRelationships(typeof(Order));
// Returns parent relationships defined by [Parent] attributes

// API endpoint for relationship discovery
GET /api/orders/relationships
{
  "entity": "Order",
  "parents": [
    {"name": "customer", "type": "Customer", "property": "CustomerId"},
    {"name": "category", "type": "Category", "property": "CategoryId"}
  ]
}
```

## Validation and Error Handling

### Compile-Time Validation
```csharp
// Framework validates parent entity has [Key] property
[Parent(typeof(Customer))] // ✅ Customer has [Key] property
public string CustomerId { get; set; }

[Parent(typeof(InvalidEntity))] // ❌ Compile-time error if no [Key] property
public string InvalidId { get; set; }
```

### Runtime Discovery
```csharp
// Service discovery of parent relationships
services.AddSingleton<IRelationshipMetadata>(provider =>
{
    var metadata = new RelationshipMetadataService();
    
    // Auto-discovers all [Parent] attributes in loaded assemblies
    metadata.ScanAssemblies(AppDomain.CurrentDomain.GetAssemblies());
    
    return metadata;
});
```

## Summary

The `ParentAttribute` provides:

- ✅ **Clear Semantics**: Obviously a parent relationship
- ✅ **Flexible Usage**: Simple and complex scenarios supported
- ✅ **Framework Integration**: Works with relationship metadata and Entity methods  
- ✅ **Migration Path**: Easy transition from ParentKeyAttribute
- ✅ **Performance**: Same resolution performance as existing system
- ✅ **Cross-Module**: Universal across all Sora modules

This design delivers the clarity and flexibility needed for a unified parent-child relationship system across the entire Sora Framework.