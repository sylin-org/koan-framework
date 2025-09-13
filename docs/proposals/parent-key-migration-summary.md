# Parent Relationship Migration: Final Implementation Summary
**Enhanced with _parent Response Structure**

## Overview

This document summarizes the migration plan for moving ParentKey functionality from Sora.Flow to Sora.Data.Core, focusing exclusively on parent relationships and the enhanced `_parent` response structure.

## Key Architectural Changes

### 1. Move from Flow-Specific to Data-Layer Universal

```csharp
// OLD: Flow-only
[ParentKey(parent: typeof(Sensor))]
public string SensorId { get; set; }

// NEW: Universal across all modules  
[Parent(typeof(Sensor))]
public string SensorId { get; set; }
```

### 2. Enhanced Response Structure with Reserved Key

**Clean separation between entity data and parent relationship data:**

```json
{
  "id": "123",
  "total": 299.99,
  "customerId": "456", 
  "categoryId": "c01",
  "_parent": {
    "customer": {"id": "456", "name": "John Doe"},
    "category": {"id": "c01", "name": "Electronics"}
  }
}
```

## Implementation Components

### Core Sora.Data.Core Components

```
src/Sora.Data.Core/
├── Relationships/
│   ├── ParentAttribute.cs      # Replaces ParentKeyAttribute
│   ├── IRelationshipMetadata.cs         # Replaces FlowRegistry (parent methods only)
│   └── RelationshipMetadataService.cs   # Implementation
```

### Enhanced EntityController with Parent Relationship Loading

**Note**: Parent relationship aggregation happens **after** set selection (pagination/filtering).

```csharp
[HttpGet("{id}")]
public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
{
  var opts = BuildOptions(); // Parses ?with= parameter
  var entity = await MyModel.GetAsync(id!, ct);

  if (entity == null) return NotFound();

  // Load parent relationships if requested (after entity selection)
  if (opts.IncludeParents.Any())
  {
    var response = await LoadParentsAsync(entity, opts.IncludeParents, ct);
    return Ok(response); // Returns object with _parent
  }

  return Ok(entity); // Returns plain entity
}

[HttpGet]
public virtual async Task<IActionResult> GetAll([FromQuery] QueryOptions opts, CancellationToken ct)
{
  // First: Apply filtering, pagination, sorting
  var entities = await MyModel.Query(opts.Filter, opts.Set, ct)
    .Skip(opts.Offset)
    .Take(opts.Limit)
    .ToListAsync();

  // Then: Load parent relationships for selected entities only
  if (opts.IncludeParents.Any())
  {
    var responses = await LoadParentsBatchAsync(entities, opts.IncludeParents, ct);
    return Ok(responses);
  }

  return Ok(entities);
}
```

### API Usage Examples

#### Query Parameters
```http
# Single parent
GET /api/orders/123?with=customer

# Multiple parents
GET /api/orders/123?with=customer,category

# JSON array format
GET /api/orders/123?with=["customer","category"]

# Load ALL available parent relationships
GET /api/orders/123?with=all
```

### Response Examples

**Basic Entity (No Relationships)**
```json
GET /api/orders/123
{
  "id": "123",
  "total": 299.99,
  "customerId": "456",
  "status": "shipped"
}
```

**With Single Parent**
```json
GET /api/orders/123?with=customer
{
  "id": "123", 
  "total": 299.99,
  "customerId": "456",
  "_parent": {
    "customer": {
      "id": "456",
      "name": "John Doe",
      "email": "john@example.com"
    }
  }
}
```

**With Multiple Parents**
```json
GET /api/orders/123?with=customer,category
{
  "id": "123",
  "total": 299.99, 
  "customerId": "456",
  "categoryId": "c01",
  "_parent": {
    "customer": {
      "id": "456",
      "name": "John Doe"
    },
    "category": {
      "id": "c01", 
      "name": "Electronics"
    }
  }
}
```

## Breaking Changes

### Files to DELETE
- ❌ `src/Sora.Flow.Core/Attributes/FlowAttributes.cs` (ParentKeyAttribute)
- ❌ `src/Sora.Flow.Core/Infrastructure/FlowRegistry.cs` (Parent methods)
- ❌ `src/Sora.Flow.Core/Services/ParentKeyResolutionService.cs`

### Component Replacements
- `ParentKeyAttribute` → `ParentAttribute`
- `FlowRegistry.GetEntityParent()` → `IRelationshipMetadata.GetParentRelationship()`
- `ParentKeyResolutionService` → parent resolution logic in Sora.Data.Core

### Sample Project Updates
```csharp
// samples/S8.Flow/S8.Flow.Shared/Reading.cs
public sealed class Reading : FlowValueObject<Reading>
{
  // BREAKING CHANGE
  [Parent(typeof(Sensor))] // Was: [ParentKey(parent: typeof(Sensor))]
  public string SensorId { get; set; } = string.Empty;
  // ... rest unchanged
}
```

## Benefits of Enhanced Structure

### 1. **Clean Architecture**
- ✅ No technical debt from compatibility layers
- ✅ Unified parent relationship system across all modules
- ✅ Clear separation between entity and parent relationship data

### 2. **Multiple Parent Support**
- ✅ Multiple parents: `_parent.customer`, `_parent.category`
- ✅ No naming conflicts with entity properties

### 3. **Enhanced Developer Experience**

#### Entity Navigation Methods
- `GetParent<T>()`: Navigate to parent entity
- `GetParents()`: Get all parent entities grouped by type

```csharp
// Type-safe parent navigation
var customer = await order.GetParent<Customer>();

// Multiple parent navigation
var order = await Order.Get("order-123");
var allParents = await order.GetParents();
// Returns: Dictionary<string, object>
// { "Customer": customerEntity, "Category": categoryEntity }
```

### 4. **Performance Benefits**
- ✅ **Batch Loading**: Single database round-trip for multiple parent relationships
- ✅ **Optional Loading**: Parents only loaded when requested
- ✅ **Provider Optimization**: SQL JOINs, MongoDB aggregations, etc.
- ✅ **Efficient Pagination**: Parents loaded only after filtering/paging

### 5. **API Evolution**
- ✅ **Backward Compatible**: Existing clients unaffected
- ✅ **Extensible**: New parent relationships added without breaking changes
- ✅ **Self-Documenting**: Clear structure shows available parent relationships

## Implementation Timeline: 6 Weeks

**Week 1-2: Foundation**
- Create Sora.Data.Core parent relationship system

**Week 3: Breaking Changes**
- Delete Flow-specific ParentKey components
- Update Flow orchestration pipeline for parent logic only

**Week 4: Sample Updates**
- Update all sample projects with ParentAttribute
- Test Flow functionality with Data-layer parent services

**Week 5: Web Enhancement**
- Implement `?with=` parameter support in EntityController for parents
- Add _parent response structure

**Week 6: Testing & Polish**
- Comprehensive testing and performance benchmarking
- Documentation updates and examples

## Success Metrics

- ✅ **Zero Breaking Changes**: For clients not using parent relationships
- ✅ **Performance Maintained**: Parent resolution speed preserved
- ✅ **Rich APIs**: Powerful parent relationship capabilities in REST endpoints
- ✅ **Cross-Module Support**: Any Entity<> can have parent relationships
- ✅ **Clean Codebase**: Unified architecture without technical debt

This migration delivers a **modern, unified parent relationship system** that enables powerful cross-module capabilities while maintaining clean separation of concerns and excellent developer experience.