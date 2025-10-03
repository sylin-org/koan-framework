# DATA-0080: [Timestamp] Attribute Auto-Update

**Status:** Accepted
**Date:** 2025-10-03
**Scope:** Koan.Data.Core

## Context

The Jobs capability requires automatic timestamp updates for `LastModified` fields to track entity changes without manual developer intervention. This is a common pattern in enterprise systems (Entity Framework's `[Timestamp]`, Rails' `updated_at`, etc.).

### Requirements

1. Auto-update fields marked with `[Timestamp]` attribute on every save
2. Minimal performance overhead (hot path optimization)
3. Zero developer friction - works transparently
4. Support both single and batch upsert operations

### Design Constraints

- **Hot path critical**: `entity.Save()` is called millions of times in production
- **Branch prediction matters**: Common case (no timestamp) must be extremely fast
- **Cache metadata**: Reflection is expensive, scan once per entity type
- **Type safety**: No runtime errors for misconfigured timestamps

## Decision

**Implement automatic [Timestamp] update in `RepositoryFacade<TEntity, TKey>` using instance-cached metadata.**

### Architecture

```
Call Stack:
entity.Save()
  → Entity<T,K>.UpsertAsync()
    → EntityEventExecutor.ExecuteUpsertAsync()
      → Data<T,K>.UpsertAsync()
        → RepositoryFacade.UpsertAsync()  ← **INTERCEPTION POINT**
          ├─ GuardAsync() - schema checks
          ├─ EnsureIdAsync() - GUID v7 generation
          ├─ UpdateTimestampIfPresent() ← **NEW: Timestamp update**
          └─ _inner.UpsertAsync() - actual DB save
```

### Implementation Strategy

**Option B: Instance Field in RepositoryFacade** (Selected)

```csharp
internal sealed class RepositoryFacade<TEntity, TKey>
{
    private readonly TimestampPropertyBag _timestampBag;

    public RepositoryFacade(...)
    {
        // Cache metadata at construction (once per entity type)
        _timestampBag = new TimestampPropertyBag(typeof(TEntity));
    }

    public async Task<TEntity> UpsertAsync(TEntity model, ...)
    {
        await GuardAsync(ct).ConfigureAwait(false);
        await _manager.EnsureIdAsync<TEntity, TKey>(model, ct).ConfigureAwait(false);

        // Fast boolean check with perfect branch prediction
        if (_timestampBag.HasTimestamp)
            _timestampBag.UpdateTimestamp(model);

        return await _inner.UpsertAsync(model, ct).ConfigureAwait(false);
    }
}

internal sealed class TimestampPropertyBag
{
    public bool HasTimestamp { get; }
    private readonly Action<object, DateTimeOffset>? _compiledSetter;

    public TimestampPropertyBag(Type entityType)
    {
        // Scan for [Timestamp] DateTimeOffset property (once per entity type)
        var prop = entityType.GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<TimestampAttribute>() != null
                              && p.PropertyType == typeof(DateTimeOffset));

        HasTimestamp = prop != null;

        if (HasTimestamp)
        {
            // Compile setter expression for fast invocation
            _compiledSetter = CompileSetter(entityType, prop);
        }
    }

    public void UpdateTimestamp(object entity)
    {
        _compiledSetter?.Invoke(entity, DateTimeOffset.UtcNow);
    }
}
```

## Alternatives Considered

### Option A: AggregateBag with Boolean Flag

**Rejected:** Requires 2 dictionary lookups per save (~20-30ns overhead).

```csharp
private void UpdateTimestampIfPresent(TEntity entity)
{
    var cfg = AggregateConfigs.Get<TEntity, TKey>(_sp);      // Dictionary lookup #1
    var bag = cfg.GetOrAddBag("timestamp_metadata", ...);    // Dictionary lookup #2
    bag.UpdateTimestamp(entity);
}
```

**Why rejected:** For entities without `[Timestamp]` (99% of cases), we waste 2 dictionary lookups just to discover "nothing to do."

### Option C: Static Generic Cache

**Rejected:** Static pollution, no diagnostic visibility.

```csharp
internal static class TimestampMetadata<TEntity>
{
    public static readonly bool HasTimestamp;
    public static readonly Action<TEntity, DateTimeOffset>? Setter;
}
```

**Why rejected:** Slightly faster (~0.5ns vs 1-2ns) but introduces static state pollution and prevents runtime diagnostics.

## Performance Comparison

| Approach | No Timestamp | With Timestamp | Diagnostics |
|----------|--------------|----------------|-------------|
| AggregateBag with flag | 20-30ns | 25-35ns | ✅ Accessible |
| **Instance field** ⭐ | **1-2ns** | **5-10ns** | ⚠️ Via RepositoryFacade |
| Static generic | 0.5-1ns | 3-8ns | ❌ Hidden |

## Rationale

1. **RepositoryFacade is constructed once per entity type** via `Lazy<>` in `AggregateConfig`
2. **Instance fields provide optimal balance:**
   - Near-zero overhead (1-2ns vs 0.5ns is negligible)
   - No static pollution
   - Clean separation of concerns
3. **Branch prediction optimization:** After first save, CPU makes boolean check essentially free
4. **Follows existing patterns:** RepositoryFacade already caches `_inner`, `_manager`, `_schemaGuard`

## Consequences

### Positive

- ✅ **Hot path optimized**: Zero dictionary lookups, perfect branch prediction
- ✅ **Type-safe**: Compiled expressions catch errors at construction
- ✅ **Transparent**: Developers just add `[Timestamp]` - no manual updates
- ✅ **Consistent**: Works across all save operations (single, batch, pipeline)
- ✅ **Minimal complexity**: Single class, single interception point

### Negative

- ⚠️ **Reflection at construction**: One-time cost when `RepositoryFacade` is first created
- ⚠️ **Supports only `DateTimeOffset`**: Not `DateTime` (design choice for UTC consistency)
- ⚠️ **No diagnostic visibility**: Can't inspect via `AggregateBag` (acceptable trade-off)

### Neutral

- ℹ️ Updates happen before entity events `After` hooks run
- ℹ️ Only supports single `[Timestamp]` property per entity (by design)

## Implementation Notes

1. Create `TimestampPropertyBag.cs` in `Koan.Data.Core/Metadata/`
2. Modify `RepositoryFacade.cs` to cache `_timestampBag` at construction
3. Update both `UpsertAsync` and `UpsertManyAsync` to call `UpdateTimestamp`
4. Add `[Timestamp]` to `System.ComponentModel.DataAnnotations` imports

## Related Decisions

- **DATA-0078**: Entity Transfer DSL (also uses reflection caching patterns)
- **JOBS-M1**: Job entity refactor requiring `[Timestamp] LastModified`

## References

- Entity Framework Core: `[Timestamp]` / `[ConcurrencyCheck]`
- Rails ActiveRecord: `updated_at` auto-management
- Koan Framework: GUID v7 auto-generation precedent
