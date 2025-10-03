# Instance-Based Relationship System: Implementation Roadmap
**Detailed Work Breakdown Structure for Semantic Methods and Streaming**

## Phase Overview

| Phase | Duration | Focus | Dependencies |
|-------|----------|-------|-------------|
| **Phase 1** | Weeks 1-2 | Entity Enhancement with Semantic Methods | None |
| **Phase 2** | Week 3 | Direct Flow Migration (Breaking Change) | Phase 1 |
| **Phase 3** | Week 4 | Web API Response Format Update | Phase 2 |

## Phase 1: Entity Enhancement with Semantic Methods (Weeks 1-2)

### Week 1: Core Instance Methods

#### Day 1-3: Entity Base Class Enhancement
```bash
# Update: src/Koan.Data.Core/Model/Entity.cs
```
**Tasks**:
- [ ] Add semantic single parent methods: `GetParent()`, `GetParent<T>()`
- [ ] Add semantic single child methods: `GetChildren()`, `GetChildren<T>()`
- [ ] Add explicit multi-relationship methods: `GetParents()`, `GetChildren<T>(propertyName)`
- [ ] Add full enrichment method: `GetRelatives()`
- [ ] Implement cardinality validation with descriptive error messages

**Critical Implementation**:
```csharp
// Semantic methods with validation
public async Task<object> GetParent(CancellationToken ct = default)
{
    var relationships = GetRelationshipService().GetParentRelationships(typeof(TEntity));
    if (relationships.Count == 0)
        throw new InvalidOperationException($"{typeof(TEntity).Name} has no parent relationships defined");
    if (relationships.Count > 1)
        throw new InvalidOperationException($"{typeof(TEntity).Name} has multiple parents. Use GetParents() or GetParent<T>(propertyName) instead");
    // Load single parent...
}
```

#### Day 4-5: Enhanced RelationshipMetadataService
```bash
# Update: src/Koan.Data.Core/Relationships/RelationshipMetadataService.cs
```
**Tasks**:
- [ ] Add cardinality validation methods: `HasSingleParent()`, `HasSingleChildType()`
- [ ] Add child type discovery: `GetAllChildTypes()`
- [ ] Implement high-performance caching with `ConcurrentDictionary`
- [ ] Add comprehensive unit tests for validation scenarios

### Week 2: Streaming Extensions and Performance

#### Day 1-3: Streaming Extension Methods
```bash
# Create: src/Koan.Data.Core/Extensions/RelationshipExtensions.cs
```
**Tasks**:
- [ ] Implement `Relatives()` extension for `IEnumerable<T>`
- [ ] Implement `Relatives()` extension for `IAsyncEnumerable<T>` with batching
- [ ] Add single entity `Relatives()` extension method
- [ ] Implement batch loading optimizations to prevent N+1 queries
- [ ] Add comprehensive unit tests for streaming scenarios

**Critical Implementation**:
```csharp
public static async IAsyncEnumerable<RelationshipGraph<TEntity>> Relatives<TEntity, TKey>(
    this IAsyncEnumerable<TEntity> entities,
    int batchSize = 100,
    [EnumeratorCancellation] CancellationToken ct = default)
    where TEntity : Entity<TEntity, TKey>, IEntity<TKey>
{
    await foreach (var enriched in entities.GetRelativesStream(batchSize, ct))
        yield return enriched;
}
```

#### Day 4-5: RelationshipGraph Response Type
```bash
# Create: src/Koan.Data.Core/Model/RelationshipGraph.cs
```
**Tasks**:
- [ ] Define `RelationshipGraph<TEntity>` with proper child grouping structure
- [ ] Implement JSON serialization optimization
- [ ] Add support for selective enrichment (only requested entities enriched)
- [ ] Create unit tests for response format validation
## Phase 2: Direct Flow Migration (Week 3)

### Breaking Change: Remove ParentKeyAttribute

#### Day 1-2: Flow Module Updates
```bash
# DELETE: src/Koan.Canon.Core/Attributes/FlowAttributes.cs (ParentKeyAttribute)
# UPDATE: src/Koan.Canon.Core/Infrastructure/FlowRegistry.cs
```
**Tasks**:
- [ ] Remove `ParentKeyAttribute` class entirely from FlowAttributes.cs
- [ ] Update FlowRegistry.cs lines 100, 135 to use `ParentAttribute`
- [ ] Adapt property references (`pk.Parent` → `pk.ParentType`)
- [ ] Test Flow orchestration with new attribute system

**Critical Code Changes**:
```csharp
// FlowRegistry.cs - Replace ParentKeyAttribute references
// BEFORE:
var pk = p.GetCustomAttribute<ParentKeyAttribute>(inherit: true);
if (pk != null) { var parentType = pk.Parent; }

// AFTER:
var pk = p.GetCustomAttribute<Koan.Data.Core.Relationships.ParentAttribute>(inherit: true);
if (pk != null) { var parentType = pk.ParentType; }
```

#### Day 3-4: ParentKeyResolutionService Migration
```bash
# UPDATE: src/Koan.Canon.Core/Services/ParentKeyResolutionService.cs
```
**Tasks**:
- [ ] Replace static resolution with instance-based methods where possible
- [ ] Maintain parked record healing functionality
- [ ] Update to use new RelationshipMetadataService
- [ ] Add fallback compatibility for complex resolution scenarios

#### Day 5: Documentation and Sample Updates
```bash
# UPDATE: docs/reference/flow-entity-lifecycle-guide.md
# UPDATE: All sample projects with ParentKey usage
```
**Tasks**:
- [ ] Replace all `[ParentKey(...)]` with `[Parent(...)]` in documentation
- [ ] Add instance method usage examples: `await entity.GetParent<T>()`
- [ ] Update sample projects (currently zero actual usage found)
- [ ] Add migration guide for existing projects

## Phase 3: Web API Response Format Update (Week 4)

### Breaking Change: RelationshipGraph Response Format

#### Day 1-2: EntityController Enhancement
```bash
# UPDATE: src/Koan.Web/Controllers/EntityController.cs
```
**Tasks**:
- [ ] Replace `_parent` response format with `RelationshipGraph<T>`
- [ ] Update `?with=all` parameter handling to use instance methods
- [ ] Add selective enrichment for specific relationships
- [ ] Implement collection enrichment support

**Critical Implementation**:
```csharp
// Replace existing parent aggregation logic with:
if (!string.IsNullOrWhiteSpace(withParam) && withParam.Contains("all"))
{
    if (model is Entity<TEntity, TKey> entity)
    {
        var enriched = await entity.GetRelatives(ct);
        return PrepareResponse(enriched);
    }
}
```

#### Day 3-4: Streaming API Support
```bash
# ADD: Streaming endpoints with relationship enrichment
```
**Tasks**:
- [ ] Add support for `GET /api/orders/stream?with=all`
- [ ] Implement batch collection enrichment for paginated results
- [ ] Add performance monitoring for streaming operations
- [ ] Test memory usage and performance with large datasets

#### Day 5: Integration Testing and Performance Validation
**Tasks**:
- [ ] End-to-end testing of new API response format
- [ ] Performance benchmarking vs. old `_parent` format
- [ ] Load testing with concurrent relationship queries
- [ ] Validate selective enrichment (only requested entities enriched)
- [ ] Test existing Flow functionality continues working

**Deliverables**:
- ✅ Instance-based relationship methods with semantic validation
- ✅ High-performance streaming support with batch operations
- ✅ Direct Flow migration without compatibility bridge overhead

## Success Metrics

### Technical Validation
- [ ] Semantic methods validate cardinality correctly
- [ ] Streaming operations handle large datasets efficiently
- [ ] Batch loading prevents N+1 query performance issues
- [ ] Flow module functions with new ParentAttribute system

### Developer Experience
- [ ] Clear error messages for cardinality violations
- [ ] IntelliSense discovery of instance methods
- [ ] Migration time ≤ 2 hours for typical Flow projects
- [ ] Performance improvement in relationship-heavy operations

This streamlined 4-week implementation focuses on the core enrichment capability at the orchestration level, delivering superior developer experience through semantic validation and clean streaming operations.
