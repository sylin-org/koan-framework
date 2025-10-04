# Proposal: Fast Count Optimization Strategy

**Status**: Draft  
**Date**: 2025-10-04  
**Author**: Framework Analysis  
**Related**: DATA-0002 (Query Capabilities), entity-capabilities-howto.md gaps analysis

---

## Executive Summary

Koan currently performs **full table scans** for all `CountAsync()` operations, even when providers offer metadata-based or index-optimized alternatives that are orders of magnitude faster. This proposal introduces a **two-tier count strategy** with capability detection to enable fast counts while maintaining correctness guarantees.

**Impact**:

- Large table counts: **30 seconds → 10ms** (Postgres reltuples)
- SQL Server metadata: **Table scan → instant** (sys.dm_db_partition_stats)
- MongoDB collections: **Scan → instant** (estimatedDocumentCount)
- Filtered counts: **Full scan → index-only** (covering indexes)

---

## Problem Statement

### Current Behavior (All Providers)

```csharp
// All of these perform FULL scans:
var total = await Todo.Count();              // ❌ Scans entire table
var filtered = await Todo.Count(t => t.Status == "PAID");  // ❌ Scans matching rows
```

**Actual SQL Generated:**

```sql
-- Postgres, SQL Server, SQLite
SELECT COUNT(1) FROM todos;  -- Full heap/clustered index scan

-- MongoDB
db.todos.countDocuments({});  -- Full collection scan
```

### Real-World Impact

**Large Table Scenario** (10M records):

- **Current**: `SELECT COUNT(1)` takes 20-45 seconds
- **With metadata**: 5-15ms (1000x faster)
- **UI Pagination**: "Showing 1-20 of ..." hangs while counting 10M rows

**Developer Experience**:

```csharp
// Developer writes what looks fast:
var page = await Todo.FirstPage(20);
Console.WriteLine($"Showing 1-20 of {page.TotalCount}");
// ❌ Hangs for 30+ seconds to count total

// Workaround (ugly):
var page = await Todo.Page(1, 20);  // Don't use FirstPage
Console.WriteLine($"Showing 1-20 of ???");  // Can't show total
```

---

## Proposed Solution

### Three-Tier Count Strategy

```csharp
public enum CountStrategy
{
    /// <summary>
    /// Exact count, may be slow (current behavior - full scan).
    /// Guaranteed accurate, no approximation.
    /// </summary>
    Exact,

    /// <summary>
    /// Fast count using metadata/statistics.
    /// May be approximate (±5% typical), but instant even on huge tables.
    /// Falls back to Exact if metadata unavailable.
    /// </summary>
    Fast,

    /// <summary>
    /// Optimized exact count using indexes.
    /// Exact like Exact, but uses index-only scans when possible.
    /// Falls back to Exact if no suitable index exists.
    /// </summary>
    Optimized
}
```

### API Design

#### Option 1: Explicit Strategy Parameter (Recommended)

`csharp
// Entity<T> static helpers expose a fluent count surface
public static EntityCountAccessor<TEntity, TKey> Count => EntityCountAccessor<TEntity, TKey>.Instance;

public sealed class EntityCountAccessor<TEntity, TKey>
where TEntity : class, IEntity<TKey>
where TKey : notnull
{
internal static readonly EntityCountAccessor<TEntity, TKey> Instance = new();

    public Task<long> Exact(CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(CountStrategy.Exact, ct);

    public Task<long> Fast(CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(CountStrategy.Fast, ct);

    public Task<long> Optimized(CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAllAsync(CountStrategy.Optimized, ct);

    public Task<long> Where(Expression<Func<TEntity, bool>> predicate, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
        => Data<TEntity, TKey>.CountAsync(predicate, strategy, ct);

}

// Usage examples
var approxTotal = await Todo.Count.Fast();
// Postgres: Uses reltuples (instant)
// SQL Server: Uses dm_db_partition_stats (instant)
// MongoDB: Uses estimatedDocumentCount() (instant)
// SQLite/JSON: Falls back to Exact

var exactFiltered = await Todo.Count.Where(
t => t.Status == "PAID",
CountStrategy.Optimized);
// Uses index-only scan if covering index exists
// Falls back to regular scan if no suitable index
`

#### Option 2: Separate Methods (More Discoverable)

```csharp
// Fast approximate counts
public static Task<long> EstimateCount(CancellationToken ct = default);
public static Task<long> EstimateCount(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);

// Optimized exact counts (index-only when possible)
public static Task<long> Count(CancellationToken ct = default);  // Now optimized by default
public static Task<long> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);

// Usage
var approx = await Todo.EstimateCount();  // Fast, may be approximate
var exact = await Todo.Count();        // Exact, uses best available method
```

#### Option 3: Capability-Aware (Framework Decides)

```csharp
// Framework automatically chooses best strategy based on capabilities
var result = await Todo.AllWithCount();
// result.TotalCount uses:
//   - Fast metadata if available and table is large (>100K rows)
//   - Optimized index-only if suitable index exists
//   - Exact full scan as last resort

// Developer can override:
var result = await Todo.AllWithCount(new DataQueryOptions
{
    CountStrategy = CountStrategy.Exact  // Force exact even if slow
});
```

---

## Provider Implementation Matrix

### Fast Count (Metadata/Approximate)

| Provider       | Implementation                                          | Accuracy                                               | Speed  | Fallback             |
| -------------- | ------------------------------------------------------- | ------------------------------------------------------ | ------ | -------------------- |
| **Postgres**   | `pg_class.reltuples` or `pg_stat_all_tables.n_live_tup` | ±5% typical, better with recent ANALYZE                | 5-10ms | Exact scan           |
| **SQL Server** | `sys.dm_db_partition_stats.row_count`                   | Exact for unpartitioned, very accurate for partitioned | 1-5ms  | Exact scan           |
| **MongoDB**    | `estimatedDocumentCount()`                              | Uses collection metadata, very accurate                | 1-10ms | `countDocuments({})` |
| **SQLite**     | N/A (no metadata)                                       | -                                                      | -      | Exact scan           |
| **Couchbase**  | Bucket stats API                                        | Accurate                                               | 5-15ms | N1QL count           |
| **Redis**      | `DBSIZE` for key count                                  | Exact                                                  | O(1)   | N/A                  |

### Optimized Count (Index-Only)

| Provider       | Strategy                            | When It Helps                        |
| -------------- | ----------------------------------- | ------------------------------------ |
| **Postgres**   | Index-only scan with visibility map | Covering index + well-vacuumed table |
| **SQL Server** | Narrow nonclustered index scan      | Query optimizer prefers narrow index |
| **MongoDB**    | Index-backed countDocuments         | Compound index matches query prefix  |
| **SQLite**     | Index btree walk                    | Index covers filter columns          |
| **Couchbase**  | GSI count pushdown                  | Index covers query predicate         |

---

## Implementation Plan

### Phase 1: Core Infrastructure (Week 1)

**Add CountStrategy and Capability Detection**

```csharp
// Koan.Data.Abstractions/QueryCapabilities.cs
[Flags]
public enum QueryCapabilities
{
    None = 0,
    String = 1 << 0,
    Linq = 1 << 1,
    FastCount = 1 << 2,      // NEW: Metadata-based count available
    OptimizedCount = 1 << 3  // NEW: Index-only count possible
}

// Koan.Data.Abstractions/IFastCountRepository.cs
public interface IFastCountRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
{
    /// <summary>
    /// Fast count using metadata/statistics. May be approximate.
    /// Returns null if not supported (caller should fall back to CountAsync).
    /// </summary>
    Task<long?> EstimateCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Fast count for filtered query using metadata. May be approximate.
    /// Returns null if not supported or filter not optimizable.
    /// </summary>
    Task<long?> EstimateCountAsync(object? query, CancellationToken ct = default);
}

// Koan.Data.Abstractions/CountStrategy.cs
public enum CountStrategy
{
    Exact,
    Fast,
    Optimized
}
```

**Update Entity<T> API**

```csharp
// Koan.Data.Core/Model/Entity.cs
public static Task<long> Count(
    CountStrategy strategy = CountStrategy.Exact,
    CancellationToken ct = default)
    => Data<TEntity, TKey>.CountAllAsync(strategy, ct);

public static Task<long> Count(
    Expression<Func<TEntity, bool>> predicate,
    CountStrategy strategy = CountStrategy.Optimized,
    CancellationToken ct = default)
    => Data<TEntity, TKey>.CountAsync(predicate, strategy, ct);

// Convenience methods
public static Task<long> EstimateCount(CancellationToken ct = default)
    => Count(CountStrategy.Fast, ct);
```

### Phase 2: Provider Implementations (Weeks 2-3)

**Postgres Implementation**

```csharp
// PostgresRepository.cs
public async Task<long?> EstimateCountAsync(CancellationToken ct = default)
{
    await using var conn = Open();

    // Use pg_stat_all_tables for more accurate estimate
    var sql = @"
        SELECT n_live_tup
        FROM pg_stat_all_tables
        WHERE schemaname = @schema AND relname = @table";

    var estimate = await conn.ExecuteScalarAsync<long?>(
        sql,
        new { schema = _options.Schema ?? "public", table = TableName });

    // Fall back to pg_class.reltuples if stats not available
    if (!estimate.HasValue || estimate.Value == 0)
    {
        sql = @"
            SELECT reltuples::bigint
            FROM pg_class
            WHERE oid = @table::regclass";
        estimate = await conn.ExecuteScalarAsync<long?>(
            sql,
            new { table = QualifiedTable });
    }

    return estimate;
}

// For optimized counts, ensure index hints in query generation
private string OptimizeCountQuery(string whereSql)
{
    // Postgres will automatically use index-only scan if possible
    // Just ensure covering index exists (user responsibility via [Index])
    return $"SELECT COUNT(*) FROM {QualifiedTable} WHERE {whereSql}";
}
```

**SQL Server Implementation**

```csharp
// SqlServerRepository.cs
public async Task<long?> EstimateCountAsync(CancellationToken ct = default)
{
    await using var conn = Open();

    // Use system DMVs for instant count
    var sql = @"
        SELECT SUM(p.rows) AS row_count
        FROM sys.partitions p
        INNER JOIN sys.objects o ON p.object_id = o.object_id
        WHERE o.name = @table
          AND o.schema_id = SCHEMA_ID(@schema)
          AND p.index_id IN (0, 1)";  -- heap or clustered index

    return await conn.ExecuteScalarAsync<long?>(
        sql,
        new { table = TableName, schema = "dbo" });
}

// Ensure narrow index for COUNT(*) optimization
private void EnsureCountOptimizationIndex(NpgsqlConnection conn)
{
    // Check if narrow index exists for count optimization
    // If not, log recommendation
    var hasNarrowIndex = CheckForNarrowIndex(conn);
    if (!hasNarrowIndex)
    {
        Logger.LogInformation(
            "Performance tip: Create narrow nonclustered index for faster COUNT(*): " +
            "CREATE NONCLUSTERED INDEX IX_{Table}_Count ON [{Table}] (Id)",
            TableName);
    }
}
```

**MongoDB Implementation**

```csharp
// MongoRepository.cs
public async Task<long?> EstimateCountAsync(CancellationToken ct = default)
{
    var collection = await GetCollectionAsync(ct);

    // Use estimatedDocumentCount for collection-level metadata
    var estimate = await collection.EstimatedDocumentCountAsync(
        cancellationToken: ct);

    return estimate;
}

public async Task<long?> EstimateCountAsync(object? query, CancellationToken ct = default)
{
    // For filtered queries, check if we can use countDocuments with hint
    // to ensure index is used
    if (query is Expression<Func<TEntity, bool>> predicate)
    {
        var collection = await GetCollectionAsync(ct);

        // Let countDocuments use index (it's already optimized)
        // Just return null to signal "use regular countDocuments"
        return null;  // Falls back to index-backed countDocuments
    }

    return null;
}
```

**SQLite Implementation**

```csharp
// SqliteRepository.cs
public Task<long?> EstimateCountAsync(CancellationToken ct = default)
{
    // SQLite has no metadata count
    return Task.FromResult<long?>(null);
}

// Note: Index-only optimization happens automatically if covering index exists
```

### Phase 3: Framework Integration (Week 3)

**Update QueryWithCount Logic**

```csharp
// Data.cs
public static async Task<QueryResult<TEntity>> AllWithCount(
    DataQueryOptions? options = null,
    CancellationToken ct = default)
{
    var repo = Repo;
    var strategy = options?.CountStrategy ?? DetermineDefaultStrategy();

    long? totalCount = null;

    // Try fast count if requested and available
    if (strategy == CountStrategy.Fast && repo is IFastCountRepository<TEntity, TKey> fastRepo)
    {
        var estimate = await fastRepo.EstimateCountAsync(ct);
        if (estimate.HasValue)
        {
            totalCount = estimate.Value;
            Logger.LogDebug("Using fast count estimate: {Count}", totalCount);
        }
    }

    // Fall back to regular count if fast not available
    if (!totalCount.HasValue)
    {
        totalCount = await repo.CountAsync(null, ct);
    }

    var items = await QueryAsync(null, options, ct);

    return new QueryResult<TEntity>
    {
        Items = items,
        TotalCount = totalCount.Value,
        IsEstimate = strategy == CountStrategy.Fast,  // NEW flag
        // ...
    };
}

private static CountStrategy DetermineDefaultStrategy()
{
    // Use Fast for large tables (if capability available)
    var caps = (Repo as IQueryCapabilities)?.Capabilities ?? QueryCapabilities.None;
    if (caps.HasFlag(QueryCapabilities.FastCount))
    {
        // Could add heuristic: use Fast only if table is "large"
        // For now, let developer decide via explicit strategy
        return CountStrategy.Exact;
    }

    return CountStrategy.Exact;
}
```

**Update QueryResult**

```csharp
// Koan.Data.Abstractions/QueryResult.cs
public class QueryResult<TEntity>
{
    public IReadOnlyList<TEntity> Items { get; init; } = Array.Empty<TEntity>();
    public long TotalCount { get; init; }
    public bool IsEstimate { get; init; }  // NEW: Indicates if TotalCount is approximate
    public int Page { get; init; }
    public int PageSize { get; init; }
    // ...
}
```

### Phase 4: Documentation & Migration (Week 4)

**Update entity-capabilities-howto.md**

Add new section after "Querying, Pagination, Streaming":

````markdown
### Fast Counts and Approximation

**Concepts**

Large tables (millions of rows) can make `Count()` very slow. Koan provides fast count strategies that use database metadata or index optimization.

**Recipe**

No additional packages. Providers that support fast counts declare `QueryCapabilities.FastCount`.

**Sample**

```csharp
// Approximate count (instant on large tables)
var estimate = await Todo.EstimateCount();
Console.WriteLine($"~{estimate:N0} todos (approximate)");

// Exact count with optimization
var exact = await Todo.Count(CountStrategy.Optimized);
Console.WriteLine($"{exact:N0} todos (exact)");

// Check capability
var caps = Data<Todo, string>.QueryCaps;
if (caps.Capabilities.HasFlag(QueryCapabilities.FastCount))
{
    // Use fast counts for pagination totals
    var page = await Todo.FirstPage(20);
    Console.WriteLine($"~{page.TotalCount} total");
}
```
````

**Provider Capabilities**

| Provider   | Fast Count   | Accuracy      | Notes                          |
| ---------- | ------------ | ------------- | ------------------------------ |
| Postgres   | ✅ reltuples | ±5% typical   | Run ANALYZE regularly          |
| SQL Server | ✅ DMV       | Exact         | Uses sys.dm_db_partition_stats |
| MongoDB    | ✅ metadata  | Very accurate | estimatedDocumentCount()       |
| SQLite     | ❌           | -             | Falls back to exact            |
| Couchbase  | ✅ stats     | Accurate      | Bucket-level metadata          |
| Redis      | ✅ DBSIZE    | Exact         | O(1) for key count             |

**Usage Scenarios**

UI pagination shows "~1.2M records" without waiting 30 seconds for exact count. Analytics dashboards display quick totals while loading. Admin panels get instant table sizes for monitoring.

**When to Use Exact vs Fast**

- **Exact**: Financial reports, compliance audits, critical business logic
- **Fast**: UI pagination, dashboards, monitoring, exploratory queries
- **Optimized**: Best of both - exact with index optimization

````

**Add ADR**

Create `docs/decisions/DATA-00XX-fast-count-strategies.md` documenting:
- Rationale for three-tier approach
- Provider capability matrix
- Migration path from current behavior
- When to use each strategy

---

## Performance Expectations

### Postgres (10M row table)

| Operation | Current | With Fast Count | Speedup |
|-----------|---------|-----------------|---------|
| `Count()` | 25-40s | 5-10ms | **4000x** |
| `Count(filter)` with index | 15-25s | 50-200ms (index-only) | **75x** |
| Paginated query total | 25s + query time | 10ms + query time | N/A |

### SQL Server (10M row table)

| Operation | Current | With Fast Count | Speedup |
|-----------|---------|-----------------|---------|
| `Count()` | 20-35s | 1-3ms (DMV) | **10000x** |
| `Count(filter)` with narrow index | 10-20s | 100-300ms | **50x** |

### MongoDB (10M doc collection)

| Operation | Current | With Fast Count | Speedup |
|-----------|---------|-----------------|---------|
| `Count()` | 15-30s | 5-15ms | **2000x** |
| `Count(filter)` indexed | 5-15s | 100-500ms | **30x** |

---

## Migration & Backwards Compatibility

### Opt-In Behavior

**Default**: All existing code continues to work exactly as before (exact counts via full scan).

```csharp
// Existing code - NO CHANGE
var count = await Todo.Count();  // Still uses exact count (full scan)
````

**Explicit Opt-In** for fast counts:

```csharp
// New code - explicit strategy
var estimate = await Todo.Count(CountStrategy.Fast);
// OR
var estimate = await Todo.EstimateCount();  // Convenience method
```

### Framework Default Strategy

Consider making `CountStrategy.Optimized` the default in a future major version:

- Uses index-only when possible (exact)
- Falls back to full scan (exact)
- Never approximates unless explicitly requested
- No behavioral change for correctness, only performance

---

## Risks & Mitigations

### Risk 1: Inaccurate Counts

**Risk**: Developers use `Fast` strategy for critical counts (financial, compliance).

**Mitigation**:

- Clear naming (`EstimateCount()` vs `Count()`)
- Documentation emphasizes accuracy trade-offs
- `IsEstimate` flag in `QueryResult`
- Default remains `Exact` strategy

### Risk 2: Provider Capability Confusion

**Risk**: Developers expect fast counts on SQLite, get disappointed.

**Mitigation**:

- Capability detection with clear fallback behavior
- Documentation includes provider matrix
- Log warnings when falling back to slower methods
- BootReport shows provider capabilities

### Risk 3: Index Optimization Requires User Action

**Risk**: Optimized counts still slow if no suitable indexes exist.

**Mitigation**:

- Log recommendations for missing indexes
- Documentation provides index creation recipes per provider
- `[Index]` attribute support (already exists in Koan)
- Health checks can validate index coverage

---

## Future Enhancements

### Adaptive Strategy

Framework automatically chooses strategy based on table size:

```csharp
// Future: Smart defaults
var result = await Todo.AllWithCount();
// Automatically uses:
//   - Fast for >1M rows
//   - Optimized for 10K-1M rows
//   - Exact for <10K rows
```

### Count Cache

For frequently accessed counts:

```csharp
[CachedCount(Duration = "5m")]
public class Todo : Entity<Todo>
{
    // Count() automatically cached for 5 minutes
}
```

### Triggered Counters

For instant exact counts on massive tables:

```csharp
[CounterTable]  // Generates triggers to maintain exact count
public class Todo : Entity<Todo>
{
    // Count() returns instant exact count from counter table
}
```

---

## Decision Points

### A. API Style

**Recommendation**: Option 2 (Separate Methods)

- Most discoverable for beginners
- Clear semantic difference (Estimate vs Count)
- No parameter overload complexity

```csharp
var approx = await Todo.EstimateCount();  // Fast, may be approximate
var exact = await Todo.Count();         // Exact, optimized when possible
```

### B. Default Strategy

**Recommendation**: Keep `Exact` as default, require explicit opt-in for `Fast`

- Backwards compatible
- No surprising approximations
- Clear developer intent

### C. Return Type

**Recommendation**: Change count return type to `long` (breaking change)

- Consistent with database row counts
- Prevents overflow on huge tables
- Microsoft uses `long` for SQL Server `COUNT_BIG`

```csharp
// Breaking change for 1.0
public static Task<long> Count(...)  // Was: Task<int>
```

---

## Acceptance Criteria

- [ ] `QueryCapabilities.FastCount` flag added
- [ ] `IFastCountRepository<T,K>` interface implemented by Postgres, SQL Server, MongoDB
- [ ] `CountStrategy` enum and parameter added to Entity<T> methods
- [ ] `EstimateCount()` convenience method added
- [ ] `QueryResult.IsEstimate` flag added
- [ ] All existing tests pass (backwards compatibility)
- [ ] New tests verify fast count behavior and fallbacks
- [ ] Performance benchmarks show 100x+ improvement on large tables
- [ ] Documentation updated with provider matrix and usage examples
- [ ] BootReport shows count capabilities per provider

---

## References

- External Analysis: Database-specific count optimization strategies
- ADR DATA-0002: Query Capabilities Flag
- `docs/guides/entity-capabilities-howto-GAPS.md`: Missing count documentation
- Provider Docs: Postgres statistics, SQL Server DMVs, MongoDB estimatedDocumentCount

---

## Next Steps

1. **Review & Approve** this proposal with framework maintainers
2. **Prototype** Postgres implementation (Week 1)
3. **Benchmark** against current behavior (target: 1000x improvement)
4. **Implement** remaining providers (Weeks 2-3)
5. **Document** with examples and provider matrix (Week 4)
6. **Release** as opt-in feature in next minor version
7. **Gather Feedback** before making optimized default in future major version
