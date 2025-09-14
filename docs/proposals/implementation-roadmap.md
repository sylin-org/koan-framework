# Parent Relationship System: Implementation Roadmap
**Detailed Work Breakdown Structure**

## Phase Overview

| Phase | Duration | Focus | Dependencies |
|-------|----------|-------|-------------|
| **Phase 1** | Weeks 1-2 | Data Layer Foundation | None |
| **Phase 2** | Weeks 3-4 | Provider Enhancements | Phase 1 |
| **Phase 3** | Weeks 5-6 | Web API Integration | Phase 2 |
| **Phase 4** | Weeks 7-8 | Flow Migration | Phase 3 |

## Phase 1: Data Layer Foundation (Weeks 1-2)

### Week 1: Core Abstractions

#### Day 1-2: ParentAttribute Implementation
```bash
# Create: src/Sora.Data.Core/Relationships/ParentAttribute.cs
```
**Tasks**:
- [ ] Define `ParentAttribute` class with simplified design (EntityType only)
- [ ] Add attribute usage validation (PropertyTargets, AllowMultiple=true)
- [ ] Write unit tests for attribute validation
- [ ] Add XML documentation

**Deliverables**:
- ✅ Simplified ParentAttribute without Role/PayloadPath
- ✅ Unit tests with 90%+ coverage
- ✅ Documentation examples

#### Day 3-4: Relationship Metadata Service
```bash
# Create: src/Sora.Data.Core/Relationships/IRelationshipMetadata.cs
# Create: src/Sora.Data.Core/Relationships/RelationshipMetadataService.cs
```
**Tasks**:
- [ ] Define `IRelationshipMetadata` interface
- [ ] Implement `RelationshipMetadataService` with reflection-based discovery
- [ ] Add caching for relationship metadata (ConcurrentDictionary)
- [ ] Write comprehensive unit tests

**Deliverables**:
- ✅ Interface for relationship metadata discovery
- ✅ High-performance cached implementation
- ✅ Support for multiple parent relationships per entity

#### Day 5: Service Registration
```bash
# Update: src/Sora.Data.Core/ServiceCollectionExtensions.cs
```
**Tasks**:
- [ ] Register `IRelationshipMetadata` as singleton
- [ ] Add auto-discovery configuration
- [ ] Create integration tests

### Week 2: Entity Navigation Methods

#### Day 1-3: Entity Base Class Enhancement
```bash
# Update: src/Sora.Data.Core/Model/Entity.cs
```
**Tasks**:
- [ ] Add `GetParent<TParent>()` static method
- [ ] Add `GetChildren<TChild>()` static method
- [ ] Add `GetAllParents()` method returning Dictionary<string, object?>
- [ ] Implement using existing `Data<TEntity, TKey>` facade pattern
- [ ] Write comprehensive unit tests

**Critical Implementation**:
```csharp
public static Task<TParent?> GetParent<TParent>(TKey childId, CancellationToken ct = default)
    where TParent : class, IEntity<TKey>
    => Data<TEntity, TKey>.GetParentAsync<TParent>(childId, ct);
```

#### Day 4-5: Flow Compatibility Bridge
```bash
# Create: src/Sora.Flow.Core/Compatibility/ParentKeyBridge.cs
```
**Tasks**:
- [ ] Create bridge that forwards Flow calls to Data layer
- [ ] Maintain exact same API for backward compatibility
- [ ] Add deprecated ParentKeyAttribute wrapper
- [ ] Test existing Flow functionality continues working

**Deliverables**:
- ✅ Zero breaking changes to existing Flow code
- ✅ Gradual migration path from Flow to Data layer

## Phase 2: Provider Enhancements (Weeks 3-4)

### Week 3: Repository Interface Extensions

#### Day 1-2: Relationship Capability Interface
```bash
# Create: src/Sora.Data.Abstractions/Relationships/IRelationshipCapabilities.cs
```
**Tasks**:
- [ ] Define capability detection interface
- [ ] Add support for JOIN-based loading capabilities
- [ ] Add batch relationship loading support
- [ ] Define RelationshipSpec for selective loading

#### Day 3-5: Repository Facade Enhancement
```bash
# Update: src/Sora.Data.Core/RepositoryFacade.cs
# Update: src/Sora.Data.Core/Data.cs
```
**Tasks**:
- [ ] Add relationship loading methods to Data facade
- [ ] Implement capability detection and fallback logic
- [ ] Add performance monitoring hooks
- [ ] Write integration tests

### Week 4: Provider-Specific Implementations

#### Day 1-2: SQL Provider Enhancement
```bash
# Update: src/Sora.Data.SqlServer/*
# Update: src/Sora.Data.PostgreSql/*
```
**Tasks**:
- [ ] Implement `IRelationshipCapabilities` in SQL providers
- [ ] Add JOIN-based relationship loading
- [ ] Optimize for common relationship patterns
- [ ] Performance testing and benchmarking

#### Day 3-4: MongoDB Provider Enhancement
```bash
# Update: src/Sora.Data.MongoDB/*
```
**Tasks**:
- [ ] Implement `$lookup` aggregation pipeline support
- [ ] Add relationship loading via MongoDB aggregation
- [ ] Test performance vs. multi-query approach
- [ ] Optimization for large datasets

#### Day 5: Simple Provider Fallback
```bash
# Update: src/Sora.Data.Json/*
```
**Tasks**:
- [ ] Implement multi-query fallback for simple providers
- [ ] Add automatic batching for performance
- [ ] Test memory usage and performance characteristics

## Phase 3: Web API Integration (Weeks 5-6)

### Week 5: EntityController Enhancement

#### Day 1-2: Query Parameter Parsing
```bash
# Create: src/Sora.Web/Extensions/RelationshipQueryExtensions.cs
```
**Tasks**:
- [ ] Implement `?with=` parameter parsing
- [ ] Support both comma-separated and JSON array formats
- [ ] Add validation for relationship names
- [ ] Handle `with=all` for loading all relationships

#### Day 3-5: Controller Integration
```bash
# Update: src/Sora.Web/Controllers/EntityController.cs
```
**Tasks**:
- [ ] Enhance `GetById` method with relationship loading
- [ ] Enhance `GetAll` method with batch relationship loading
- [ ] Add RelationshipGraph response format
- [ ] Implement pagination-aware relationship loading

**Critical Implementation**:
```csharp
[HttpGet("{id}")]
public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct)
{
    var entity = await Data<TEntity, TKey>.GetAsync(id, ct);
    if (entity == null) return NotFound();

    var relationships = Request.Query.ParseWithParameter();
    if (relationships.Any())
    {
        var graph = await Data<TEntity, TKey>.GetWithRelationships(id, relationships, ct);
        return Ok(graph);
    }

    return Ok(entity);
}
```

### Week 6: Response Format and Testing

#### Day 1-2: RelationshipGraph Implementation
```bash
# Create: src/Sora.Web/Models/RelationshipGraph.cs
```
**Tasks**:
- [ ] Define RelationshipGraph<TEntity> response model
- [ ] Implement clean separation between entity and relationship data
- [ ] Add support for multiple parent and child relationships
- [ ] JSON serialization optimization

#### Day 3-5: Integration Testing
**Tasks**:
- [ ] Create comprehensive API tests for relationship loading
- [ ] Test performance with large datasets
- [ ] Test different provider capabilities
- [ ] Load testing for concurrent relationship queries

## Phase 4: Flow Migration (Weeks 7-8)

### Week 7: Flow Module Updates

#### Day 1-2: Service Registration Updates
```bash
# Update: src/Sora.Flow.Core/ServiceCollectionExtensions.cs
```
**Tasks**:
- [ ] Register Data-layer relationship services
- [ ] Initialize compatibility bridge
- [ ] Replace ParentKeyResolutionService with new implementation
- [ ] Add transition logging

#### Day 3-5: Sample Project Migration
```bash
# Update: samples/S8.Flow/S8.Flow.Shared/*.cs
```
**Tasks**:
- [ ] Replace `[ParentKey]` with `[Parent]` attributes
- [ ] Test Flow orchestration with new relationship system
- [ ] Update documentation and examples
- [ ] Performance validation

### Week 8: Documentation and Cleanup

#### Day 1-2: Documentation Updates
**Tasks**:
- [ ] Update API documentation with relationship loading examples
- [ ] Create migration guide for existing projects
- [ ] Add performance optimization guide
- [ ] Update sample project documentation

#### Day 3-5: Final Testing and Optimization
**Tasks**:
- [ ] End-to-end testing of complete system
- [ ] Performance benchmarking and optimization
- [ ] Security review of relationship loading
- [ ] Load testing with realistic workloads

## Implementation Commands

### Phase 1 Commands
```bash
# Week 1: Core Abstractions
dotnet new classlib -n Sora.Data.Core.Relationships
dotnet add src/Sora.Data.Core reference Sora.Data.Abstractions
dotnet test src/Sora.Data.Core.Tests --filter Category=Relationships

# Week 2: Entity Enhancements
dotnet build src/Sora.Data.Core
dotnet test src/Sora.Flow.Core.Tests --filter Category=Compatibility
```

### Phase 2 Commands
```bash
# Week 3: Interface Extensions
dotnet add src/Sora.Data.Abstractions package System.ComponentModel.Annotations
dotnet build src/Sora.Data.Core

# Week 4: Provider Updates
dotnet build src/Sora.Data.SqlServer
dotnet build src/Sora.Data.MongoDB
dotnet test src/Sora.Data.Tests.Integration --filter Category=Relationships
```

### Phase 3 Commands
```bash
# Week 5: Web Integration
dotnet add src/Sora.Web reference Sora.Data.Core
dotnet build src/Sora.Web

# Week 6: Testing
dotnet test src/Sora.Web.Tests --filter Category=Relationships
dotnet run --project samples/WebApi.Sample --urls=http://localhost:5000
```

### Phase 4 Commands
```bash
# Week 7: Flow Migration
dotnet build src/Sora.Flow.Core
dotnet test src/Sora.Flow.Tests --filter Category=Migration

# Week 8: Final Validation
dotnet test --collect:"XPlat Code Coverage"
dotnet build --configuration Release
```

## Validation Checklist

### Phase 1 Validation
- [ ] ParentAttribute compiles and validates correctly
- [ ] RelationshipMetadataService discovers relationships accurately
- [ ] Entity navigation methods delegate to Data facade correctly
- [ ] Existing Flow functionality unaffected by bridge

### Phase 2 Validation
- [ ] SQL providers use JOINs for relationship loading
- [ ] MongoDB providers use aggregation pipelines
- [ ] Performance benchmarks show improvement over N+1 queries
- [ ] Fallback to multi-query works for simple providers

### Phase 3 Validation
- [ ] `?with=customer,category` parameter parsing works
- [ ] RelationshipGraph response format correct
- [ ] Pagination with relationships performs acceptably
- [ ] Error handling for invalid relationship names

### Phase 4 Validation
- [ ] Sample projects run with Parent attributes
- [ ] Flow orchestration works with new relationship system
- [ ] Migration guide tested with real projects
- [ ] Performance matches or exceeds original implementation

## Success Criteria

### Technical Success
- **Zero Breaking Changes**: Existing Flow code continues working
- **Performance Maintained**: Relationship loading time ≤ current implementation
- **Memory Efficient**: Peak memory usage ≤ 110% of current
- **Provider Agnostic**: Same API across all data providers

### Developer Experience Success
- **Migration Time**: Flow project migration ≤ 4 hours for typical project
- **API Discoverability**: Relationship methods discoverable via IntelliSense
- **Documentation Quality**: Complete examples for common scenarios
- **Error Messages**: Clear, actionable error messages for relationship issues

This roadmap provides a concrete, executable plan for implementing the unified parent relationship system while maintaining zero breaking changes and delivering enhanced cross-module capabilities.