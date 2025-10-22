# DATA-0081: InMemory Data Adapter

**Status**: Accepted
**Date**: 2025-10-03
**Deciders**: Framework Architecture Team
**Related**: DATA-0080 (Timestamp Auto-Update), JOBS-001 (Jobs v1.0)

## Context

During implementation of Jobs v1.0 and [Timestamp] auto-update (DATA-0080), we discovered an architectural inconsistency:

### The Problem

**InMemoryJobStore bypasses the Koan.Data framework entirely:**

```csharp
// InMemoryJobStore: Direct storage bypass
public Task<Job> CreateAsync(Job job, ...) {
    // Manual TimestampPropertyBag implementation
    if (_timestampBag.HasTimestamp)
        _timestampBag.UpdateTimestamp(job);

    var stored = _jobs.GetOrAdd(job.Id, _ => new StoredJob(job));
    // Direct ConcurrentDictionary - no framework features
}

// EntityJobStore: Uses framework
public Task<Job> CreateAsync(Job job, ...) {
    await job.Save(cancellationToken);  // → RepositoryFacade → Adapter
    // Gets timestamp, events, audit, schema validation automatically
}
```

**This creates two classes of storage with different behavioral guarantees.**

### Implications

1. **Inconsistent Cross-Cutting Concerns**
   - InMemoryJobStore requires manual implementation of every framework feature
   - Timestamp auto-update works via framework in EntityJobStore, manually in InMemoryJobStore
   - Future features (audit, events, schema validation) must be duplicated

2. **Testing Gaps**
   - Unit tests using InMemoryJobStore don't validate framework code paths
   - Different behavior between test and production storage
   - Can't trust that tests verify production behavior

3. **Violates Framework Principles**
   - **"Provider Transparency"**: Code should work identically across storage backends
   - **"Reference = Intent"**: Adding package should enable functionality
   - Currently, developers must know which storage bypasses framework

4. **Architectural Debt**
   - Every specialized storage implementation tempted to bypass framework
   - Cross-cutting concerns must be reimplemented per storage type
   - Maintenance burden grows linearly with storage types

## Decision

**Create Koan.Data.Connector.InMemory as a first-class data adapter.**

### Key Principles

1. **Full Framework Integration**
   - Implements `IDataAdapterFactory` and `IDataRepository<TEntity, TKey>`
   - All cross-cutting concerns (timestamp, events, audit, schema) work automatically
   - Same code path as production adapters (PostgreSQL, MongoDB, etc.)

2. **Complete Capability Reporting**
   ```csharp
   QueryCapabilities = LinqQueries | Projections | Count | OrderBy | Pagination
   WriteCapabilities = Upsert | UpsertMany | Delete | DeleteMany | Batch
   ```

3. **Thread-Safe, Partition-Aware Storage**
   - `ConcurrentDictionary` per entity type per partition
   - Respects `EntityContext.With(partition: "tenant-123")`
   - Supports multi-tenant testing scenarios

4. **Auto-Registration**
   - `KoanAutoRegistrar` makes it available when referenced
   - Can be default fallback adapter (no DB required for development)
   - Priority: Lowest (real adapters override)

## Consequences

### Positive

1. **Architectural Consistency**
   - ✅ Single code path for all storage operations
   - ✅ Framework features work identically across all adapters
   - ✅ No special cases or bypass logic

2. **Superior Testing Experience**
   ```csharp
   // Unit tests - same code path as production
   services.AddKoan();  // InMemory auto-registers as fallback

   var todo = new Todo { Title = "Test" };
   await todo.Save();  // Uses RepositoryFacade → InMemoryAdapter

   // Tests validate:
   // - Timestamp auto-update ✓
   // - Entity events ✓
   // - Schema validation ✓
   // - Audit trails ✓
   ```

3. **Developer Experience**
   - ✅ "Reference = Intent": Add package, storage works
   - ✅ Zero configuration for development/testing
   - ✅ Same API surface across all environments

4. **Maintainability**
   - ✅ Cross-cutting concerns implemented once in RepositoryFacade
   - ✅ New features automatically work with InMemory adapter
   - ✅ Single codebase to maintain

5. **Future-Proof**
   - ✅ Template for other adapters (Redis, Memcached, etc.)
   - ✅ No temptation to bypass framework
   - ✅ Clear architectural boundaries

### Negative

1. **Performance Overhead (Negligible)**
   - Direct: ~10ns (ConcurrentDictionary.GetOrAdd)
   - Via Framework: ~212ns (RepositoryFacade + adapter + storage)
   - **Impact**: 0.002% of typical job operation (1-10ms)
   - **Verdict**: Acceptable for dev/test scenarios

2. **Migration Required**
   - InMemoryJobStore must be refactored to use Data<Job> framework
   - Manual TimestampPropertyBag implementation removed
   - Low risk: Internal implementation detail, public API unchanged

### Neutral

1. **Additional Package**
   - Adds `Koan.Data.Connector.InMemory` to connector ecosystem
   - Consistent with existing pattern (Postgres, MongoDB, SQLite)
   - ~500 LOC implementation + tests

## Implementation Strategy

### Phase 1: Core Adapter (This ADR)

```
src/Connectors/Data/InMemory/
├── Koan.Data.Connector.InMemory.csproj
├── InMemoryAdapter.cs              // IDataAdapterFactory
├── InMemoryRepository.cs           // IDataRepository<TEntity, TKey>
├── InMemoryDataStore.cs            // Thread-safe storage manager
├── InMemoryBatchSet.cs             // IBatchSet implementation
└── Initialization/
    └── KoanAutoRegistrar.cs        // Auto-registration
```

### Phase 2: Migration (Future)

1. Refactor InMemoryJobStore to use `Data<Job>.Upsert()`
2. Remove manual TimestampPropertyBag from InMemoryJobStore
3. Mark specialized storage implementations as `[Obsolete]` with migration path

### Phase 3: Documentation (Future)

1. Update testing guide to recommend InMemory adapter
2. Document adapter development pattern
3. Add examples for multi-tenant testing

## Validation Criteria

### Functional Requirements

- [ ] Full CRUD operations (Get, Query, Upsert, Delete)
- [ ] Batch operations (Add, Update, Delete in single transaction)
- [ ] LINQ query support (via LINQ-to-Objects)
- [ ] Partition/multi-tenancy support via EntityContext
- [ ] Capability reporting (QueryCapabilities, WriteCapabilities)
- [ ] Thread-safe concurrent operations
- [ ] Auto-registration via KoanAutoRegistrar

### Framework Integration

- [ ] [Timestamp] auto-update works automatically
- [ ] Entity events (OnBeforeSave, OnAfterLoad) fire correctly
- [ ] Schema validation executes
- [ ] Audit trails capture operations (when enabled)
- [ ] GUID v7 generation for Entity<T>

### Testing

- [ ] Unit tests for all operations
- [ ] Concurrent access tests (thread safety)
- [ ] Multi-partition isolation tests
- [ ] Integration tests with Entity<T> patterns
- [ ] Performance benchmarks (verify overhead acceptable)

## References

- **Issue**: Jobs v1.0 implementation revealed architectural inconsistency
- **Related ADRs**:
  - DATA-0080: [Timestamp] Attribute Auto-Update
  - JOBS-001: Jobs v1.0 Proposal (Milestone 1 cleanup)
- **Framework Principles**: Provider transparency, "Reference = Intent"

## Notes

### Why Not Optional?

Some might argue InMemory adapter is "just for testing" and shouldn't be first-class.

**Counter-argument:**
1. Testing is a universal requirement for all Koan applications
2. Framework consistency is non-negotiable
3. Developer experience depends on zero-friction development workflow
4. Architectural principles (provider transparency) must apply universally

### Performance Philosophy

**Framework overhead is acceptable when:**
- Provides universal consistency
- Eliminates architectural debt
- Enables superior testing experience
- Cost is negligible relative to operation time (<1%)

InMemory adapter meets all these criteria.

---

**Decision Maker**: Framework Architect
**Implementation**: DATA-0081 Implementation Task
**Review Date**: After Phase 2 migration (3 months)
