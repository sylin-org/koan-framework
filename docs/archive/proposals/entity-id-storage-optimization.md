# Entity ID Storage Optimization - Refactoring Proposal

## Executive Summary

This proposal outlines a comprehensive refactoring of Koan's Entity<> ID handling to enable automatic storage optimization while maintaining zero-configuration developer experience.

## Problem Statement

**Current Issue**: All Entity<> IDs are stored as string/VARCHAR/TEXT in databases regardless of the underlying TKey type, causing:

- **Storage Inefficiency**: PostgreSQL GUIDs stored as 36+ character TEXT vs 16-byte native UUID (4x overhead)
- **Performance Impact**: 3-5x slower queries due to string-based indexing vs native types

**Key Constraint**: Must maintain `Entity<T>` as string-keyed to preserve:

- API contract stability (REST endpoints remain string-based)
- Zero breaking changes to existing codebase

## Solution Architecture

### Core Principle: Transparent Adapter-Level Optimization

**Entity Layer**: Remains string-based for developer consistency
**Storage Layer**: Uses optimal native types with transparent conversion
**API Layer**: Unchanged string-based contracts

```csharp
// Developer code: Unchanged
public class Product : Entity<Product>  // string ID
{
    public string Name { get; set; }
}

// Storage: Automatically optimized
// PostgreSQL: id UUID (16 bytes)
// API: GET /api/products/550e8400-e29b-41d4-a716-446655440000
```

### Bootstrap-Time Analysis & Caching

**Entity Analysis Pipeline**:

1. Scan all Entity<> types at application startup
2. Analyze ID patterns to determine optimization potential
3. Cache conversion strategies per entity type
4. Configure adapter behavior based on analysis

**Storage Optimization Detection**:

- Entities with GUID-pattern string IDs
- Explicit optimization attributes (future)
- Conservative fallback to string storage

## Implementation Phases

### Phase 1: Core Infrastructure (Week 1-2)

- **EntityStorageCache<T>**: Bootstrap-time analysis and caching
- **StorageOptimization**: Conversion strategy abstraction
- **Enhanced DDL Generation**: Optimal column type selection

### Phase 2: Adapter Integration (Week 3-4)

- **PostgreSQL Adapter**: UUID storage with string conversion
- **SQL Server Adapter**: UNIQUEIDENTIFIER storage with string conversion
- **MongoDB Adapter**: BinData UUID with string conversion
- **Transparent CRUD Operations**: Get/Upsert/Delete with conversion

### Phase 4: Testing & Documentation (Week 7-8)

- **Comprehensive Test Coverage**: All providers and conversion scenarios
- **Performance Benchmarks**: Quantify improvements
- **Migration Documentation**: Deployment and monitoring guidance

## Technical Architecture

### Adapter Conversion Layer

```csharp
internal class PostgresRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
{
    private static readonly StorageOptimization Opt = EntityStorageCache<TEntity>.Optimization;

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        var storageId = Opt.ToStorage(id.ToString()!);  // string → Guid

        var sql = $"SELECT id, json FROM {TableName} WHERE id = @id";
        var result = await _connection.QuerySingleOrDefaultAsync(sql, new { id = storageId });

        if (result == null) return null;

        var entity = JsonConvert.DeserializeObject<TEntity>(result.json);
        entity.Id = (TKey)(object)Opt.FromStorage(result.id);  // Guid → string
        return entity;
    }
}
```

## Performance Impact

### Expected Improvements

| Database   | Optimization                | Storage Reduction | Query Performance | Index Size |
| ---------- | --------------------------- | ----------------- | ----------------- | ---------- |
| PostgreSQL | TEXT → UUID                 | 56% (36b → 16b)   | +300%             | -75%       |
| SQL Server | NVARCHAR → UNIQUEIDENTIFIER | 78% (72b → 16b)   | +200%             | -70%       |
| MySQL      | VARCHAR → BINARY(16)        | 56% (36b → 16b)   | +150%             | -60%       |

### Runtime Overhead

- **Conversion Cost**: ~100ns per operation (Guid.Parse/ToString)
- **Database I/O**: ~1ms per operation
- **Net Overhead**: <0.01% (negligible vs 3x performance gain)

## Migration Strategy

### Zero-Downtime Deployment

1. **Deploy Code**: New conversion logic handles both string and native storage
2. **Background Migration**: Gradually convert existing tables to optimal types
3. **Validation**: Verify conversion accuracy and performance improvements
4. **Cleanup**: Remove legacy string storage support

### Rollback Strategy

- **Graceful Degradation**: System continues working with string storage if optimization fails
- **Feature Flag**: Runtime toggle for enabling/disabling optimization
- **Data Safety**: All conversions are reversible (Guid ↔ string)

## Risk Assessment

### Low Risk

- **API Compatibility**: No breaking changes to external interfaces
- **Data Safety**: All ID conversions are lossless and reversible
- **Performance**: Minimal runtime overhead with significant gains

### Medium Risk

- **Detection Accuracy**: False positives/negatives in optimization detection
- **Conversion Bugs**: Edge cases in string ↔ native type conversion
- **Migration Complexity**: Large dataset conversion timing

### Mitigation Strategies

- **Conservative Detection**: Default to string storage when uncertain
- **Comprehensive Testing**: All conversion scenarios and edge cases
- **Gradual Rollout**: Feature flags and progressive enablement
- **Monitoring**: Real-time performance and error tracking

## Success Metrics

### Technical Metrics

- **Storage Efficiency**: 50%+ reduction in ID column storage
- **Query Performance**: 2-5x improvement in lookup operations
- **Index Performance**: 3-4x improvement in index scan operations

### Developer Experience Metrics

- **Zero Configuration**: No additional developer setup required
- **API Stability**: 100% compatibility with existing REST endpoints
- **Flow System**: Maintained canonical resolution functionality

## Next Steps

1. **Create Proposal Structure**: Tracking documents and implementation phases
2. **Implement Core Infrastructure**: EntityStorageCache and StorageOptimization
3. **PostgreSQL Adapter**: First provider implementation with testing
4. **Flow Integration**: Validate canonical resolution compatibility
5. **Multi-Provider Rollout**: SQL Server, MySQL, MongoDB adapters
6. **Performance Validation**: Benchmark improvements and production testing

---

**Document Version**: 1.0
**Status**: Proposal
**Next Review**: Implementation Phase 1 Completion
