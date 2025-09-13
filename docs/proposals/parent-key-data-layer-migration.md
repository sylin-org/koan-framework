# RFC: Migrate ParentKey Functionality from Sora.Flow to Sora.Data

**Status:** Draft  
**Author:** Framework Team  
**Date:** 2024-09-12  
**Priority:** High  

## Executive Summary

This proposal recommends migrating the ParentKey functionality from `Sora.Flow.Core` to `Sora.Data.Core` to enable cross-module parent-child relationship support. This architectural change will unlock GraphQL-like relationship querying in REST APIs, improve framework consistency, and reduce coupling between modules.

## Current State Analysis

### ParentKey in Sora.Flow

Currently, ParentKey functionality is tightly coupled to Flow-specific concepts:

- **Location**: `src/Sora.Flow.Core/Attributes/FlowAttributes.cs:44`
- **Dependencies**: FlowEntity, FlowValueObject, IdentityLink, FlowSets, FlowRegistry
- **Resolution Service**: `ParentKeyResolutionService` handles deferred parent resolution
- **Usage**: 37+ files across Flow module, 3 sample implementations

```csharp
// Current implementation
[ParentKey(parent: typeof(Sensor), payloadPath: "SensorId")]
public string SensorId { get; set; } = string.Empty;
```

### Limitations

1. **Module Isolation**: Only Flow entities can use parent-child relationships
2. **Code Duplication**: Other modules must implement custom relationship patterns  
3. **Inconsistent APIs**: No unified approach to relationship loading across modules
4. **Limited Querying**: No support for relationship inclusion in REST endpoints

## Proposed Architecture

### New Abstractions in Sora.Data.Core

```csharp
// Core relationship attribute
[AttributeUsage(AttributeTargets.Property)]
public class EntityReferenceAttribute : Attribute
{
    public Type ParentType { get; }
    public string? Role { get; }
    public string? ExternalKeyPath { get; }
    
    public EntityReferenceAttribute(Type parentType, string? role = null, string? externalKeyPath = null)
    {
        ParentType = parentType ?? throw new ArgumentNullException(nameof(parentType));
        Role = string.IsNullOrWhiteSpace(role) ? null : role;
        ExternalKeyPath = string.IsNullOrWhiteSpace(externalKeyPath) ? null : externalKeyPath;
    }
}

// Relationship resolution abstraction
public interface IRelationshipResolver
{
    Task<TKey?> ResolveParentKeyAsync<TParent, TKey>(
        string externalKey, 
        string sourceSystem, 
        CancellationToken ct = default) where TParent : IEntity<TKey>;
        
    Task<IReadOnlyList<TChild>> GetChildrenAsync<TChild, TKey>(
        TKey parentId, 
        CancellationToken ct = default) where TChild : class, IEntity<TKey>;
        
    Task<TParent?> GetParentAsync<TParent, TKey>(
        TKey childId, 
        CancellationToken ct = default) where TParent : class, IEntity<TKey>;
}

// Enhanced Entity base class
public abstract class Entity<TEntity, TKey> : IEntity<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // Existing functionality...
    
    // New relationship methods
    public static Task<TParent?> GetParent<TParent>(TKey childId, CancellationToken ct = default)
        where TParent : class, IEntity<TKey>
        => Data<TEntity, TKey>.Relationships.GetParent<TParent>(childId, ct);
    
    public static Task<IReadOnlyList<TChild>> GetChildren<TChild>(TKey parentId, CancellationToken ct = default)
        where TChild : class, IEntity<TKey>
        => Data<TEntity, TKey>.Relationships.GetChildren<TChild>(parentId, ct);
}
```

### Relationship Registry

```csharp
public interface IRelationshipRegistry
{
    void RegisterRelationship<TEntity, TParent, TKey>(
        Expression<Func<TEntity, TKey>> foreignKey,
        string? role = null) 
        where TEntity : class, IEntity<TKey>
        where TParent : class, IEntity<TKey>;
        
    RelationshipInfo? GetRelationship<TEntity>(string relationshipName);
    IEnumerable<RelationshipInfo> GetRelationships<TEntity>();
}

public record RelationshipInfo(
    Type EntityType,
    Type RelatedType, 
    string PropertyName,
    string? Role,
    RelationshipCardinality Cardinality);
```

## Implementation Roadmap

### Phase 1: Foundation (Weeks 1-3)
- [ ] Create `EntityReferenceAttribute` in Sora.Data.Core
- [ ] Implement `IRelationshipResolver` interface and basic implementation
- [ ] Add relationship methods to `Entity<TEntity, TKey>` base class
- [ ] Create `IRelationshipRegistry` for metadata management
- [ ] Maintain `ParentKeyAttribute` as bridge/alias in Sora.Flow

### Phase 2: Provider Support (Weeks 4-9)  
- [ ] Extend SQL providers with JOIN-based relationship resolution
- [ ] Implement MongoDB aggregation pipeline for relationships
- [ ] Add JSON provider relationship support via in-memory resolution
- [ ] Create `IDeferredResolutionStore` abstraction for parked records
- [ ] Implement relationship-aware batch operations

### Phase 3: Sora.Web Integration (Weeks 6-10)
- [ ] Enhance `QueryOptions` with relationship loading support
- [ ] Implement `?with=` parameter parsing in `EntityController`
- [ ] Add batch relationship loading to prevent N+1 queries
- [ ] Create relationship caching and performance monitoring
- [ ] Update OpenAPI documentation with relationship discovery

### Phase 4: Flow Migration (Weeks 10-14)
- [ ] Create adapter layer for Flow's ParentKeyResolutionService
- [ ] Migrate FlowRegistry to use Data-layer relationship registry
- [ ] Update Flow orchestration to use generalized relationship resolution
- [ ] Maintain backward compatibility during transition
- [ ] Deprecate Flow-specific ParentKey implementations

### Phase 5: Advanced Features (Weeks 15-18)
- [ ] Implement nested relationship loading (`?with=customer.address`)
- [ ] Add relationship filtering (`?with=items&items.filter={"active":true}`)
- [ ] Create custom relationship projection support
- [ ] Add cascade operation support (delete parent → delete children)
- [ ] Performance optimization and monitoring enhancements

## Benefits Analysis

### Cross-Module Consistency
- **Unified Relationship Model**: Single approach across all Sora modules
- **Reduced Code Duplication**: Shared relationship resolution logic
- **Consistent Developer Experience**: Same patterns everywhere

### Enhanced API Capabilities

#### REST Endpoints with Relationship Loading
```http
# Basic entity retrieval
GET /api/orders/123

# With customer information
GET /api/orders/123?with=customer

# With multiple relationships
GET /api/orders/123?with=customer,items,shipping

# Collection with relationships
GET /api/customers?with=orders&filter={"active":true}
```

#### Response Structure
```json
{
  "id": "123",
  "total": 299.99,
  "status": "shipped",
  "customer": {
    "id": "456", 
    "name": "John Doe",
    "email": "john@example.com"
  },
  "items": [
    {
      "id": "789",
      "name": "Product A", 
      "quantity": 2,
      "price": 149.99
    }
  ]
}
```

### Performance Benefits
- **Batch Loading**: Single database round-trip for multiple relationships
- **Provider Optimization**: SQL JOINs, MongoDB aggregations, etc.
- **Intelligent Caching**: Relationship-aware caching strategies
- **N+1 Query Prevention**: Built-in protection mechanisms

### Developer Experience Improvements
- **Type-Safe Relationships**: Compile-time relationship validation
- **Enhanced Tooling**: IntelliSense support for relationship navigation
- **Improved Documentation**: Auto-generated relationship schemas
- **Consistent Patterns**: Same relationship patterns across all modules

## Impact Assessment

### Breaking Changes
- **Sora.Flow Module**: 37 files require updates to use new abstractions
- **Migration Path**: Bridge pattern maintains compatibility during transition
- **Timeline**: 6-month deprecation period for old ParentKey usage

### Performance Impact
- **Positive**: Batch loading reduces database queries
- **Positive**: Provider-specific optimizations (SQL JOINs, etc.)
- **Neutral**: Memory usage slightly increased for relationship metadata
- **Monitoring**: Built-in performance tracking and warnings

### Maintenance Considerations
- **Reduced Complexity**: Unified relationship handling reduces maintenance burden
- **Improved Testing**: Centralized relationship logic easier to test
- **Better Documentation**: Clear separation between data persistence and business logic

## Risk Mitigation

### Technical Risks
- **Database Performance**: Implement query analysis and optimization
- **Memory Usage**: Implement relationship result caching with TTL
- **Complexity**: Provide clear migration guides and tooling

### Migration Risks  
- **Breaking Changes**: Maintain bridge compatibility for 6 months
- **Data Integrity**: Extensive testing during migration phase
- **Rollback Plan**: Ability to revert to Flow-specific implementation

## Success Metrics

### Adoption Metrics
- **API Usage**: Increased usage of `?with=` parameter in REST endpoints
- **Developer Feedback**: Positive sentiment on simplified relationship patterns
- **Documentation Views**: Increased engagement with relationship documentation

### Performance Metrics
- **Query Reduction**: 50%+ reduction in database queries for relationship loading
- **Response Time**: Maintained or improved response times with batch loading
- **Memory Usage**: Stable memory consumption despite enhanced functionality

### Quality Metrics
- **Bug Reports**: Reduced relationship-related bug reports
- **Code Coverage**: Maintained or improved test coverage
- **Maintainability**: Reduced cyclomatic complexity in relationship handling

## Conclusion

Moving ParentKey functionality from Sora.Flow to Sora.Data represents a strategic architectural improvement that:

1. **Enables Cross-Module Relationships**: All Sora modules can benefit from unified relationship patterns
2. **Unlocks GraphQL-like REST APIs**: Enhanced querying capabilities without GraphQL complexity  
3. **Improves Framework Consistency**: Unified approach to parent-child relationships
4. **Reduces Maintenance Burden**: Centralized relationship handling logic
5. **Enhances Developer Experience**: Type-safe, well-documented relationship APIs

The phased implementation approach minimizes risk while delivering immediate value through enhanced Sora.Web capabilities. The 18-week timeline provides adequate time for thorough testing and migration without disrupting existing functionality.

**Recommendation: Proceed with implementation** using the proposed phased approach, starting with Phase 1 foundation work to establish core abstractions while maintaining backward compatibility.