# Koan Framework: Universal Instance-Based Relationship System
**RFC: Instance-Based Semantic Relationships with Streaming Support**

## Executive Summary

**Objective**: Replace Flow-specific `ParentKeyAttribute` with universal instance-based relationship methods that provide semantic validation and high-performance streaming operations.

**Impact**: All Koan modules gain intuitive relationship navigation through instance methods like `model.GetParent()`, `model.GetChildren()`, and `stream.Relatives()`.

**Breaking Change**: Direct replacement of `ParentKeyAttribute` with `ParentAttribute` - no compatibility bridge.

## Current State vs. Target Architecture

### Current: Flow-Only Static Parent Relationships
```csharp
// Limited to Flow module only
[ParentKey(parent: typeof(Sensor), role: "Primary", payloadPath: "device.sensor.id")]
public string SensorId { get; set; }

// Complex static resolution required
var parent = await SomeStaticResolutionService.ResolveParent(...);
```

### Target: Universal Instance-Based Relationships
```csharp
// Works across ALL Koan modules with semantic validation
[Parent(typeof(Sensor))]
public string SensorId { get; set; }

// Intuitive instance methods
var sensor = await reading.GetParent<Sensor>();        // Typed single parent
var parent = await reading.GetParent();               // Semantic single parent (validated)
var allParents = await reading.GetParents();          // Multiple parents
var children = await sensor.GetChildren<Reading>();   // Typed children
var allChildren = await sensor.GetChildren();         // Semantic single child type (validated)

// Clean streaming syntax
await foreach (var enriched in Data<Reading, string>.AllStream().Relatives())
{
    // Process enriched entities with parents/children loaded
}
```

## Key Design Decisions

### 1. **Instance-Based Semantic Methods**
- **PRINCIPLE**: Relationship navigation through intuitive instance methods
- **VALIDATION**: Backend validates cardinality - `GetParent()` only works for single-parent models
- **FLEXIBILITY**: Explicit methods (`GetParent<T>()`, `GetChildren<T>()`) for complex scenarios

### 2. **Direct Migration Strategy**
- **NO BRIDGE**: Remove `ParentKeyAttribute` entirely - direct replacement with `ParentAttribute`
- **RATIONALE**: Eliminates compatibility layer complexity and maintenance overhead
- **BENEFIT**: Clean, single relationship system across all modules

### 3. **Selective Enrichment**
- **PRINCIPLE**: Only requested entities receive enriched format (`RelationshipGraph<T>`)
- **RAW RELATIONSHIPS**: Parents and children remain as raw entities (no recursive enrichment)
- **PERFORMANCE**: Prevents cascade loading and maintains predictable response sizes

### 4. **Streaming-First Architecture**
- **CLEAN SYNTAX**: `Data<T>.AllStream().Relatives()` vs verbose `AllStreamWithRelatives()`
- **BATCH OPERATIONS**: Collection enrichment with `List<Order>.Relatives()`
- **ASYNC STREAMING**: Support for `IAsyncEnumerable<T>` with batching

## Implementation Specification

### Core Components

#### 1. ParentAttribute (Koan.Data.Core)
```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ParentAttribute : Attribute
{
    public Type EntityType { get; }

    public ParentAttribute(Type entityType)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    }
}
```

#### 2. Enhanced Relationship Metadata Service
```csharp
public interface IRelationshipMetadata
{
    IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType);
    IReadOnlyList<(string ReferenceProperty, Type ChildType)> GetChildRelationships(Type parentType, Type childType);
    IReadOnlyList<Type> GetAllChildTypes(Type parentType);
    bool HasSingleParent(Type entityType);
    bool HasSingleChildType(Type entityType);
    bool HasSingleChildRelationship<TChild>(Type entityType);
}

public class RelationshipMetadataService : IRelationshipMetadata
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<(string, Type)>> _parentCache = new();
    private readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> _allChildTypesCache = new();

    // High-performance cached implementations with cardinality validation
}
```

#### 3. Instance-Based Entity Navigation Methods
```csharp
public abstract class Entity<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // ✅ Semantic single parent methods (validated)
    public async Task<object> GetParent(CancellationToken ct = default)
    {
        // Validates: exactly one parent relationship exists
        // Throws: InvalidOperationException if multiple parents
    }

    public async Task<TParent> GetParent<TParent>(CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
    {
        // Validates: exactly one relationship to TParent exists
        // Throws: InvalidOperationException if multiple TParent relationships
    }

    // ✅ Explicit parent methods (no validation)
    public async Task<TParent?> GetParent<TParent>(string propertyName, CancellationToken ct = default)
        where TParent : class, IEntity<TKey>;

    public async Task<Dictionary<string, object?>> GetParents(CancellationToken ct = default);

    // ✅ Semantic single child methods (validated)
    public async Task<IReadOnlyList<object>> GetChildren(CancellationToken ct = default)
    {
        // Validates: exactly one child type exists
        // Throws: InvalidOperationException if multiple child types
    }

    public async Task<IReadOnlyList<TChild>> GetChildren<TChild>(CancellationToken ct = default)
        where TChild : class, IEntity<TKey>
    {
        // Validates: exactly one TChild relationship exists
        // Throws: InvalidOperationException if multiple TChild relationships
    }

    // ✅ Explicit child methods (no validation)
    public async Task<IReadOnlyList<TChild>> GetChildren<TChild>(string referenceProperty, CancellationToken ct = default)
        where TChild : class, IEntity<TKey>;

    // ✅ Full enrichment method
    public async Task<RelationshipGraph<TEntity>> GetRelatives(CancellationToken ct = default);
}
```

#### 4. Streaming Extensions for Batch Operations
```csharp
public static class RelationshipExtensions
{
    // ✅ Collection enrichment
    public static async Task<IReadOnlyList<RelationshipGraph<TEntity>>> Relatives<TEntity, TKey>(
        this IEnumerable<TEntity> entities,
        CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
        where TKey : notnull;

    // ✅ Async streaming enrichment
    public static async IAsyncEnumerable<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
        this IAsyncEnumerable<TEntity> entities,
        int batchSize = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
        where TKey : notnull;

    // ✅ Single entity enrichment
    public static async Task<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
        this TEntity entity,
        CancellationToken ct = default)
        where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
        where TKey : notnull;
}
```

#### 5. Clean Data Facade Streaming Syntax
```csharp
// ✅ Preferred streaming syntax
await foreach (var enriched in Data<Order, string>.AllStream().Relatives())
{
    // Process enriched orders with parents/children loaded
}

await foreach (var enriched in Data<Order, string>.QueryStream(o => o.Total > 100).Relatives(batchSize: 50))
{
    // Process high-value orders with relationships
}

// ✅ Batch collection operations
var orders = await Data<Order, string>.All();
var enrichedOrders = await orders.Relatives();
```

#### 6. Updated REST API Integration
```csharp
[HttpGet("{id}")]
public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
{
    var model = await Data<TEntity, TKey>.GetAsync(id, ct);
    if (model == null) return NotFound();

    var withParam = HttpContext.Request.Query.TryGetValue("with", out var w) ? w.ToString() : null;

    if (!string.IsNullOrWhiteSpace(withParam) && withParam.Contains("all"))
    {
        if (model is Entity<TEntity, TKey> entity)
        {
            var enriched = await entity.GetRelatives(ct);
            return PrepareResponse(enriched);
        }
    }

    return PrepareResponse(model);
}
```

### API Response Format and Usage Examples

#### Basic Entity (No Enrichment)
```json
GET /api/orders/123
{
  "id": "123",
  "customerId": "456",
  "categoryId": "c01",
  "total": 299.99,
  "status": "shipped"
}
```

#### Enriched Entity with Selective Loading
```json
GET /api/orders/123?with=all
{
  "entity": {
    "id": "123",
    "customerId": "456",
    "categoryId": "c01",
    "total": 299.99,
    "status": "shipped"
  },
  "parents": {
    "CustomerId": {"id": "456", "name": "John Doe", "email": "john@example.com"},
    "CategoryId": {"id": "c01", "name": "Electronics"}
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
        {"id": "review-1", "orderId": "123", "rating": 5, "comment": "Great order!"}
      ]
    }
  }
}
```

#### Code Usage Examples

**Semantic Single Parent/Child:**
```csharp
// ✅ Single parent models
var orderItem = await Data<OrderItem, string>.GetAsync("item-123");
var order = await orderItem.GetParent<Order>();      // Works: OrderItem has one parent
var parent = await orderItem.GetParent();           // Works: Returns Order as object

// ❌ Multiple parent models
var order = await Data<Order, string>.GetAsync("order-456");
try
{
    var parent = await order.GetParent();  // Throws: Order has multiple parents
}
catch (InvalidOperationException ex)
{
    // "Order has multiple parents. Use GetParents() or GetParent<T>(propertyName) instead"
}

// ✅ Explicit methods work for multi-parent scenarios
var customer = await order.GetParent<Customer>();
var allParents = await order.GetParents();
```

**Streaming and Batch Operations:**
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

// ✅ High-performance batch streaming
await foreach (var enriched in Data<Order, string>
    .QueryStream(o => o.Total > 100)
    .Relatives(batchSize: 50))
{
    await ProcessEnrichedOrder(enriched);
}
  }
}
```

### Performance Optimization Strategy

#### Orchestration-Level Batch Loading
The relationship enrichment is implemented at the orchestration level using the existing Data facade:

```csharp
// Batch loading to prevent N+1 queries
private async Task<Dictionary<string, object?>> LoadAllParents(IEnumerable<TEntity> entities, CancellationToken ct)
{
    var relationshipService = GetRelationshipService();
    var relationships = relationshipService.GetParentRelationships(typeof(TEntity));
    var parentDict = new Dictionary<string, object?>();

    foreach (var (propertyName, parentType) in relationships)
    {
        // Extract all parent IDs for this relationship
        var parentIds = entities
            .Select(e => typeof(TEntity).GetProperty(propertyName)?.GetValue(e))
            .Where(id => id != null)
            .Cast<TKey>()
            .Distinct()
            .ToList();

        if (parentIds.Any())
        {
            // Batch load all parents using existing Data facade
            var parents = await LoadParentsBatch(parentType, parentIds, ct);
            parentDict[propertyName] = parents;
        }
    }

    return parentDict;
}
```

## Implementation Roadmap

### Phase 1: Entity Enhancement with Semantic Methods (Weeks 1-2)
**Deliverables**:
- ✅ Extend Entity base class with semantic instance methods
- ✅ Add cardinality validation with descriptive error messages
- ✅ Implement caching in RelationshipMetadataService
- ✅ Create streaming extension methods

**Critical Path**:
```bash
# Week 1: Core Entity Methods
src/Koan.Data.Core/Model/Entity.cs
  - GetParent() / GetParent<T>() with validation
  - GetChildren() / GetChildren<T>() with validation
  - GetParents() / GetChildren<T>(propertyName) explicit methods
  - GetRelatives() enrichment method

# Week 2: Extensions and Performance
src/Koan.Data.Core/Extensions/RelationshipExtensions.cs
  - Relatives() extension for IEnumerable<T>
  - Relatives() extension for IAsyncEnumerable<T>
  - Batch loading optimizations with child type discovery
```

### Phase 2: Direct Flow Migration (Week 3)
**Breaking Change Approach**:
- ❌ Remove ParentKeyAttribute entirely from FlowAttributes.cs
- ✅ Update FlowRegistry.cs to use ParentAttribute
- ✅ Replace ParentKeyResolutionService with instance-based resolution
- ✅ Migrate all Flow documentation examples

**Critical Files**:
```bash
src/Koan.Canon.Core/Attributes/FlowAttributes.cs     # DELETE ParentKeyAttribute
src/Koan.Canon.Core/Infrastructure/FlowRegistry.cs   # Use ParentAttribute
src/Koan.Canon.Core/Services/ParentKeyResolutionService.cs  # Use instance methods
docs/reference/flow-entity-lifecycle-guide.md       # Update examples
```

### Phase 3: Web API Response Format Migration (Week 4)
**Breaking Change**:
- ✅ Replace `_parent` format with RelationshipGraph structure
- ✅ Update EntityController to use instance methods
- ✅ Add selective enrichment based on ?with=all parameter
- ✅ Support streaming endpoints with enrichment

**New API Endpoints**:
```bash
GET /api/orders/123?with=all                # Full enrichment
GET /api/orders/stream?with=all             # Streaming enrichment
GET /api/orders?with=all&page=1             # Batch enrichment
```

## Direct Migration Strategy

### No Backward Compatibility Bridge
**Decision**: Remove `ParentKeyAttribute` entirely - no compatibility layer

**Rationale**:
- Eliminates maintenance overhead of dual attribute systems
- Forces clean migration to superior instance-based API
- Reduces confusion about which relationship system to use
- Current analysis shows minimal actual ParentKeyAttribute usage in samples

### Migration Steps

#### 1. Flow Module Updates
```csharp
// DELETE from FlowAttributes.cs
public sealed class ParentKeyAttribute : Attribute { ... }

// UPDATE FlowRegistry.cs (lines 100, 135)
// BEFORE:
var pk = p.GetCustomAttribute<ParentKeyAttribute>(inherit: true);

// AFTER:
var pk = p.GetCustomAttribute<Koan.Data.Core.Relationships.ParentAttribute>(inherit: true);
if (pk != null)
{
    var parentType = pk.ParentType; // Adapt property name
    // Continue with existing logic...
}
```

#### 2. Documentation Migration
```csharp
// Before: Static resolution
[ParentKey(typeof(Device))]
public string DeviceId { get; set; }

// After: Instance methods
[Parent(typeof(Device))]
public string DeviceId { get; set; }

// NEW usage patterns:
var device = await sensor.GetParent<Device>();
var readings = await device.GetChildren<Reading>();
var enriched = await sensor.GetRelatives();
```

#### 3. Sample Project Migration
- Replace all `[ParentKey(...)]` with `[Parent(...)]`
- Update documentation examples to use instance methods
- Add streaming usage examples with new `.Relatives()` syntax

## Success Metrics

### Technical Metrics
- **Instance-Based API**: All relationship navigation through intuitive instance methods
- **Semantic Validation**: Backend validates cardinality with descriptive error messages
- **Streaming Performance**: High-performance batch operations with `.Relatives()` syntax
- **Selective Enrichment**: Only requested entities get enriched format (no recursive loading)
- **Clean Architecture**: Single relationship system across all Koan modules

### Developer Experience Metrics
- **Migration Time**: Flow project migration ≤ 2 hours (direct replacement)
- **API Discoverability**: `model.GetParent()` discoverable via IntelliSense
- **Error Clarity**: Clear validation messages for cardinality violations
- **Streaming Adoption**: Increased usage of `Data<T>.AllStream().Relatives()` pattern
- **Performance**: Batch loading eliminates N+1 queries in relationship navigation

## Risk Mitigation

### Technical Risks
- **Performance Impact**: Mitigated by orchestration-level batch loading and selective enrichment
- **Query Complexity**: Mitigated by depth limits and selective relationship loading
- **Memory Usage**: Mitigated by configurable relationship batch sizes and streaming support

### Migration Risks
- **Breaking Changes**: Direct replacement approach requires coordinated migration but eliminates complexity
- **Feature Regression**: Mitigated by comprehensive testing and validation of semantic methods
- **Rollback Plan**: Clean git revert possible due to direct replacement approach

## Conclusion

This unified parent relationship system delivers:

1. **Cross-Module Consistency**: All Koan modules can use parent relationships
2. **Enhanced REST APIs**: GraphQL-like querying without GraphQL complexity
3. **Performance Optimization**: Provider-specific relationship loading strategies
4. **Clean Architecture**: Proper separation of concerns with performance escape hatches
5. **Zero Disruption**: Backward compatibility during migration

The phased implementation approach minimizes risk while delivering immediate value through enhanced cross-module capabilities and improved developer experience.

**Recommendation**: Proceed with implementation using this specification as the definitive guide for the parent relationship migration.