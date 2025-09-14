# Sora Framework: Universal Parent Relationship System
**RFC: Migrate from Flow-specific to Cross-Module Parent Relationships**

## Executive Summary

**Objective**: Move parent relationship functionality from `Sora.Flow` to `Sora.Data.Core` to enable cross-module parent-child relationships and GraphQL-like REST API querying.

**Impact**: Enables all Sora modules to use parent relationships while maintaining existing Flow functionality.

**Timeline**: 8-week phased implementation with zero breaking changes.

## Current State vs. Target Architecture

### Current: Flow-Only Parent Relationships
```csharp
// Limited to Flow module only
[ParentKey(parent: typeof(Sensor), role: "Primary", payloadPath: "device.sensor.id")]
public string SensorId { get; set; }
```

### Target: Universal Parent Relationships
```csharp
// Works across ALL Sora modules
[Parent(typeof(Sensor))]
public string SensorId { get; set; }

// Enhanced REST API support
GET /api/readings/123?with=sensor,device
```

## Key Design Decisions

Based on architectural analysis and current implementation gaps:

### 1. **Simplified Attribute Design**
- **REMOVE**: `Role` and `PayloadPath` parameters (unused in current implementation)
- **RATIONALE**: Property names provide semantic clarity (`SourceAccountId` vs `Role="Source"`)
- **BENEFIT**: Cleaner migration path and reduced complexity

### 2. **Hybrid Architecture for Data Traversal**
- **PRINCIPLE**: Relationship navigation stays above data layer for clean separation
- **OPTIMIZATION**: Allow capable providers to implement performance enhancements via capability interfaces
- **FALLBACK**: Multi-query approach for simple providers

### 3. **Comprehensive Relationship Support**
- **MULTIPLE PARENTS**: Support entities with Customer, Category, Region parents
- **MULTIPLE CHILDREN**: Support Customer having Orders, Reviews, Addresses children
- **TYPE SAFETY**: Maintain compile-time relationship validation

## Implementation Specification

### Core Components

#### 1. ParentAttribute (Sora.Data.Core)
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

#### 2. Relationship Metadata Service
```csharp
public interface IRelationshipMetadata
{
    IReadOnlyList<(string PropertyName, Type ParentType)> GetParentRelationships(Type entityType);
    IReadOnlyList<RelationshipInfo> GetAllRelationships(Type entityType);
}

public record RelationshipInfo(
    string PropertyName,
    Type ParentType,
    RelationshipDirection Direction,
    string? Role = null
);
```

#### 3. Enhanced Entity Navigation Methods
```csharp
public abstract class Entity<TEntity, TKey>
{
    // Individual relationship navigation
    public static Task<TParent?> GetParent<TParent>(TKey childId, CancellationToken ct = default)
        where TParent : class, IEntity<TKey>;

    public static Task<IReadOnlyList<TChild>> GetChildren<TChild>(TKey parentId, CancellationToken ct = default)
        where TChild : class, IEntity<TKey>;

    // Bulk relationship loading for performance
    public static Task<RelationshipGraph<TEntity>> GetWithRelationships(
        TKey entityId,
        RelationshipSpec[] includes,
        CancellationToken ct = default);
}
```

#### 4. REST API Integration
```csharp
// EntityController enhancement
[HttpGet("{id}")]
public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
{
    var entity = await Data<TEntity, TKey>.GetAsync(id, ct);
    if (entity == null) return NotFound();

    // Parse ?with= parameter
    var relationships = Request.Query.ParseWithParameter();
    if (relationships.Any())
    {
        var graph = await LoadRelationshipsAsync(entity, relationships, ct);
        return Ok(graph);
    }

    return Ok(entity);
}
```

### API Response Format

#### Basic Entity
```json
GET /api/orders/123
{
  "id": "123",
  "customerId": "456",
  "total": 299.99
}
```

#### With Parent Relationships
```json
GET /api/orders/123?with=customer,category
{
  "entity": {
    "id": "123",
    "customerId": "456",
    "categoryId": "c01",
    "total": 299.99
  },
  "parents": {
    "customerId": {"id": "456", "name": "John Doe"},
    "categoryId": {"id": "c01", "name": "Electronics"}
  }
}
```

### Performance Optimization Strategy

#### Provider Capability Detection
```csharp
public interface IRelationshipCapabilities<TEntity, TKey>
{
    bool SupportsJoinLoading { get; }
    Task<RelationshipGraph<TEntity>> LoadWithJoins(TKey id, RelationshipSpec[] includes, CancellationToken ct);
}
```

#### Provider-Specific Optimizations
- **SQL Providers**: Use JOIN queries for efficient relationship loading
- **MongoDB**: Leverage `$lookup` aggregation pipelines
- **Simple Providers**: Fall back to multi-query approach with automatic batching
- **Framework**: Automatically detect and use best available strategy

## Implementation Roadmap

### Phase 1: Data Layer Foundation (Weeks 1-2)
**Deliverables**:
- ✅ Create `ParentAttribute` in Sora.Data.Core
- ✅ Implement `IRelationshipMetadata` and `RelationshipMetadataService`
- ✅ Add basic navigation methods to Entity base class
- ✅ Create compatibility bridge for Flow migration

**Critical Path**:
```bash
# Week 1
src/Sora.Data.Core/Relationships/ParentAttribute.cs
src/Sora.Data.Core/Relationships/IRelationshipMetadata.cs
src/Sora.Data.Core/Relationships/RelationshipMetadataService.cs

# Week 2
src/Sora.Data.Core/Model/Entity.cs  # Add navigation methods
src/Sora.Flow.Core/Compatibility/ParentKeyBridge.cs  # Migration bridge
```

### Phase 2: Provider Enhancements (Weeks 3-4)
**Deliverables**:
- ✅ Extend repository interfaces with relationship capabilities
- ✅ Implement SQL provider JOIN-based relationship loading
- ✅ Implement MongoDB aggregation pipeline support
- ✅ Add performance monitoring and capability detection

### Phase 3: Web API Integration (Weeks 5-6)
**Deliverables**:
- ✅ Enhance EntityController with `?with=` parameter support
- ✅ Implement relationship loading in REST endpoints
- ✅ Add RelationshipGraph response format
- ✅ Create API documentation and examples

### Phase 4: Flow Migration (Weeks 7-8)
**Deliverables**:
- ✅ Update Flow module to use Data-layer relationships
- ✅ Migrate sample projects from ParentKey to Parent attributes
- ✅ Update Flow orchestration to use new relationship services
- ✅ Deprecate Flow-specific ParentKeyAttribute with 6-month timeline

## Migration Strategy

### Backward Compatibility
```csharp
// Bridge pattern maintains compatibility during migration
namespace Sora.Flow.Core.Compatibility
{
    [Obsolete("Use [Parent] from Sora.Data.Core instead. Will be removed in v2.0")]
    public sealed class ParentKeyAttribute : ParentAttribute
    {
        public Type Parent => EntityType;  // Compatibility property

        public ParentKeyAttribute(Type parent, string? role = null, string? payloadPath = null)
            : base(parent)
        {
            // Ignore role/payloadPath for simplified design
        }
    }
}
```

### Sample Project Updates
```csharp
// Before: Flow-specific
[ParentKey(parent: typeof(Sensor))]
public string SensorId { get; set; }

// After: Universal
[Parent(typeof(Sensor))]
public string SensorId { get; set; }
```

## Success Metrics

### Technical Metrics
- **Zero Breaking Changes**: Existing code continues working during migration
- **Performance Maintained**: Parent resolution speed preserved or improved
- **Cross-Module Support**: Any Entity<> can define parent relationships
- **API Enhancement**: REST endpoints support GraphQL-like relationship loading

### Adoption Metrics
- **Migration Success**: >95% of Flow ParentKey usages migrated successfully
- **API Usage**: Increased usage of `?with=` parameter in REST endpoints
- **Developer Feedback**: Positive sentiment on unified relationship patterns

## Risk Mitigation

### Technical Risks
- **Performance Impact**: Mitigated by provider capability detection and automatic optimization
- **Query Complexity**: Mitigated by depth limits and selective relationship loading
- **Memory Usage**: Mitigated by configurable relationship batch sizes

### Migration Risks
- **Breaking Changes**: Mitigated by compatibility bridge and gradual migration approach
- **Feature Regression**: Mitigated by comprehensive testing and parallel execution
- **Rollback Plan**: Bridge can fallback to original Flow implementation if needed

## Conclusion

This unified parent relationship system delivers:

1. **Cross-Module Consistency**: All Sora modules can use parent relationships
2. **Enhanced REST APIs**: GraphQL-like querying without GraphQL complexity
3. **Performance Optimization**: Provider-specific relationship loading strategies
4. **Clean Architecture**: Proper separation of concerns with performance escape hatches
5. **Zero Disruption**: Backward compatibility during migration

The phased implementation approach minimizes risk while delivering immediate value through enhanced cross-module capabilities and improved developer experience.

**Recommendation**: Proceed with implementation using this specification as the definitive guide for the parent relationship migration.