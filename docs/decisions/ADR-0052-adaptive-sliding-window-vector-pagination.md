# ADR-0052: Adaptive Sliding Window Pagination for Vector Search

**Status:** Accepted
**Date:** 2025-01-04
**Context:** S5.Recs infinite scroll with 130k+ media items
**Decision Makers:** Architecture review
**Affected Components:** S5.Recs.Services.RecsService, S5.Recs.Services.BandCacheService

---

## Context and Problem Statement

The S5.Recs application uses Weaviate vector search for personalized media recommendations. With 130k+ media items and growing, the current pagination approach creates severe UX limitations:

**Current Implementation Problems:**
1. **TopK-only pagination** - Fetching `topK=offset+limit` from vector database
   - Example: Page 10 (offset 1000, limit 100) requires fetching top 1100 items, then slicing [1000:1100]
   - TopK capped at 1000 for performance → **brick wall at page 10**
   - User Experience: "Cannot scroll past page 10" despite having 129,000 more items

2. **Memory inefficiency** - Each page request refetches all previous items
   - Page 5: Fetch 500 items, return 100 (80% waste)
   - Page 10: Fetch 1000 items, return 100 (90% waste)

3. **Personalization overhead** - Every fetch repeats expensive operations:
   - Title boost calculation (Levenshtein distance)
   - Genre/tag matching
   - Censor penalty application
   - Score blending (vector 40% + popularity 30% + boosts 30%)

**User Experience Requirements:**
- Users expect to scroll **infinitely** through all 130k+ items
- Performance critical: Sub-100ms response time is expected
- Consistent page sizes (always 100 items per page)
- Smooth scrolling (no visible loading delays)

---

## Decision Drivers

1. **Infinite Depth** - Must support pagination through entire 130k+ dataset
2. **Performance** - <100ms response time for pages 1-100+
3. **Memory Bounded** - Fixed memory per user (~4MB regardless of scroll depth)
4. **Deterministic Ordering** - Same query = same results within session
5. **Personalization Preserved** - All scoring/boosting logic must work unchanged
6. **Bidirectional Scrolling** - Users can scroll back up without refetching

---

## Considered Options

### Option 1: Exclusion-Based Pagination (id NOT IN [...])
**Approach:** Track seen IDs, exclude them from next query.

```sql
-- Pseudo-query
SELECT * FROM media
WHERE id NOT IN (@seen_ids)
ORDER BY score DESC
LIMIT 100
```

**Pros:**
- Simple implementation (single filter parameter)
- Works with existing Weaviate setup
- Deterministic results

**Cons:**
- ❌ Filter cost grows linearly with depth (1800 IDs at page 20)
- ❌ Weaviate evaluates exclusion filter on every candidate
- ❌ Performance degrades at deep pages (>200ms at page 50)
- ❌ Not stateless (frontend must track all seen IDs)

**Estimated Limits:** Breaks around page 50 (5000 excluded IDs)

---

### Option 2: Offset/Limit with Large TopK
**Approach:** Fetch `topK=10000`, apply pagination in-memory.

```csharp
var results = await Vector.Search(topK: 10000);
return results.Skip(offset).Take(limit);
```

**Pros:**
- Simple implementation
- Covers ~100 pages

**Cons:**
- ❌ Still has brick wall (at page 100 instead of page 10)
- ❌ Massive memory usage (10k items × 2KB = 20MB per request)
- ❌ Weaviate performance degrades with large topK
- ❌ 90%+ waste (fetch 10k, return 100)

**Verdict:** Just pushes the problem further, doesn't solve it

---

### Option 3: Score-Band Pagination (Threshold Paging)
**Approach:** Paginate through score ranges instead of fixed offsets.

```
Page 0: score >= 4.0, limit 100
Page 1: 3.5 <= score < 4.0, limit 100
Page 2: 3.0 <= score < 3.5, limit 100
```

**Pros:**
- ✅ Stateless (no tracking required)
- ✅ Infinite depth
- ✅ Scales with score distribution

**Cons:**
- ❌ **Variable page sizes** - band might have 20 items or 800 items
- ❌ Unpredictable "how many pages total?"
- ❌ Requires careful threshold tuning per dataset
- ❌ Score normalization complexity

**Mitigation Needed:** Adaptive band widening to guarantee page sizes

---

### Option 4: Adaptive Sliding Window Cache with Score Bands ✅ **SELECTED**
**Approach:** Combine score-band filtering with an in-memory sliding window cache.

```
Cache Structure per (userId, queryHash):
┌─────────────────────────────────────────────┐
│  [-1000 items ← Current Position → +1000]   │
│                                              │
│  Item 0    Item 500    Item 1000  Item 2000│
│  [4.8]     [3.5]       [2.8]      [2.1]    │
│    ↑                     ↑                   │
│  Upper                 Lower                 │
│  Bound                 Bound                 │
└─────────────────────────────────────────────┘

User scrolls to offset 1700 (approaching lower bound):
→ Trigger: Fetch band [2.1 → 1.8], get 500 items
→ Overlay: Merge new items, resort by (score, id)
→ Flush: Remove items 0-500 (far from position)
→ New window: Items 500-2500
```

**Pros:**
- ✅ **Infinite depth** - No brick wall, scales to millions
- ✅ **Performance** - <1ms for cache hits (95% of requests)
- ✅ **Memory bounded** - Fixed 2000 items × 2KB = 4MB per user
- ✅ **Bidirectional** - Can scroll up or down
- ✅ **Adaptive** - Bands auto-widen in sparse regions
- ✅ **Guaranteed page sizes** - Synchronous widening ensures 100 items
- ✅ **Preserves personalization** - All scoring logic works unchanged

**Cons:**
- ⚠️ Implementation complexity (moderate)
- ⚠️ Requires score-based filtering after personalization
- ⚠️ Cache warm-up for first page (150-250ms)

---

## Decision

**Adopt Option 4: Adaptive Sliding Window Cache with Score Bands**

### Architecture

#### 1. **Cache Structure**
```csharp
public class SlidingWindowCache
{
    // Sorted by (score DESC, id ASC) for deterministic tie-breaking
    public SortedList<(double Score, string Id), Recommendation> Items { get; set; }

    public double UpperScoreBound { get; set; } = 1.0;
    public double LowerScoreBound { get; set; } = 0.9;

    public int WindowSize { get; set; } = 2000;  // Keep 2000 items max
    public int PrefetchThreshold { get; set; } = 300;  // Trigger at 300 items from edge

    public DateTimeOffset LastAccessed { get; set; }
    public string QueryHash { get; set; }  // Hash of (text, filters, userId, sort)
}
```

#### 2. **Initial Fetch with Guaranteed Fulfillment**
```csharp
// User requests page 0 (100 items)
var cache = await InitializeCacheAsync(query, pageSize: 100);

// Algorithm:
1. Target: 500 items (5 pages buffer)
2. Fetch band [1.0 → 0.9]
   - If < 500 items: Widen to [0.9 → 0.8] and fetch more
   - Repeat until 500 items OR max 10 attempts
3. Adaptive widening: Sparse bands → increase width by 1.5×
4. Return items [0:100]
```

**Guaranteed Page Size:** If band [1.0 → 0.7] returns only 50 items, synchronously widen to [0.7 → 0.5] until `>= 100` items collected.

#### 3. **Sliding Window Maintenance**
```csharp
// User at offset 1700, approaching lower bound (cache size 2000)
if (currentPosition > cache.Items.Count - 300) {
    // Background fetch next band
    _ = Task.Run(async () => {
        var nextBand = await FetchScoreBandAsync(
            scoreMin: cache.LowerScoreBound - 0.1,
            scoreMax: cache.LowerScoreBound,
            targetSize: 500
        );

        // Merge and evict
        lock (cache) {
            foreach (var item in nextBand) {
                cache.Items[(item.Score, item.Media.Id)] = item;
            }

            // Evict top items (keep window at 2000)
            while (cache.Items.Count > 2000) {
                cache.Items.RemoveAt(0);  // Remove highest scored (oldest)
            }
        }
    });
}
```

#### 4. **Score Filtering Strategy**
Since S5.Recs computes final scores **after** vector search:
```
Final Score = (0.4 × vectorScore) + (0.3 × popularity) + (0.2 × genreBoost)
              + titleBoost + preferBoost - spoilerPenalty - censorPenalty
```

**Filter after personalization:**
```csharp
// 1. Fetch large pool from Weaviate (topK=1000)
var rawResults = await Vector.Search(topK: 1000);

// 2. Apply personalization (existing logic)
var personalized = await ScoreAndPersonalize(rawResults, ...);

// 3. Filter by score band
var bandFiltered = personalized
    .Where(r => r.Score >= scoreMin && r.Score < scoreMax)
    .OrderByDescending(r => r.Score)
    .ThenBy(r => r.Media.Id)  // Deterministic tie-breaking
    .ToList();
```

#### 5. **Adaptive Band Widening**
```csharp
var bandWidth = 0.1;  // Start with 10% score range
var attempts = 0;

while (items.Count < targetSize && attempts < 10) {
    var batch = await FetchScoreBand(scoreMin, scoreMax);
    items.AddRange(batch);

    if (items.Count >= targetSize) break;

    // Sparse region detected
    if (batch.Count < targetSize / 2) {
        bandWidth *= 1.5;  // 0.1 → 0.15 → 0.225
        _logger.LogDebug("Sparse region, widening band to {Width:F3}", bandWidth);
    }

    scoreMin -= bandWidth;
    attempts++;
}
```

---

## Consequences

### Positive
- ✅ **Removes UX brick wall** - Users can scroll through all 130k+ items
- ✅ **Performance improvement** - Pages 1-100 respond in <1ms (cache hits)
- ✅ **Memory efficiency** - 4MB per active user vs. 20MB+ with large topK
- ✅ **Predictable costs** - Memory and compute costs scale with concurrent users, not dataset size
- ✅ **Future-proof** - Architecture scales to millions of items

### Negative
- ⚠️ **Cache warm-up cost** - First page takes 150-250ms (acceptable for UX)
- ⚠️ **Memory overhead** - 4MB × 100 concurrent users = 400MB (manageable)
- ⚠️ **Implementation complexity** - ~500 LOC for cache service + tests

### Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Cache memory explosion with many users | LRU eviction with 10-minute TTL |
| Stale cache after data updates | Invalidate cache on media import/update |
| Band underfilling in sparse regions | Adaptive widening guarantees min page size |
| Race conditions on cache updates | `lock (cache)` for merge operations |
| Query parameter changes mid-scroll | QueryHash includes all params, cache miss triggers new init |

---

## Performance Characteristics

### Expected Latency
| Page Range | Scenario | Latency | Notes |
|------------|----------|---------|-------|
| Page 0 | Cold cache | 150-250ms | Fetch 500 items, personalize |
| Pages 1-4 | Hot cache | <1ms | Slice from memory |
| Page 5+ | Hot cache + prefetch | <5ms | Background fetch |
| Page 100 | Hot cache | <1ms | Still fast! |

### Memory Usage
- **Per user:** 2000 items × 2KB = 4MB
- **100 concurrent users:** 400MB total
- **Cache overhead:** ~10% (indexes, metadata)

### Database Load
- **Cold start:** 1-3 queries to fill cache (500-1500 items)
- **Steady state:** 1 query per 5 pages (background prefetch)
- **Deep scroll (page 100):** ~20 queries total (amortized 5 pages per query)

---

## Implementation Checklist

### Phase 1: Core Infrastructure
- [ ] Create `SlidingWindowCache` class
- [ ] Create `IBandCacheService` interface
- [ ] Implement `BandCacheService` with IMemoryCache
- [ ] Add query hash generation (text + filters + userId + sort)
- [ ] Implement LRU eviction with 10-minute TTL

### Phase 2: Band Fetching
- [ ] Implement `FetchScoreBandAsync` with score filtering
- [ ] Add adaptive band widening logic
- [ ] Implement guaranteed page fulfillment (synchronous widening)
- [ ] Add logging for band fetch metrics

### Phase 3: Sliding Window Logic
- [ ] Implement `GetPageAsync` with edge detection
- [ ] Add background prefetch task (triggered at 300 items from edge)
- [ ] Implement bidirectional prefetch (upper + lower bounds)
- [ ] Add thread-safe merge and eviction

### Phase 4: Integration
- [ ] Update `RecsService.QueryAsync` to use band cache
- [ ] Remove topK=1000 safety limit
- [ ] Update frontend to use offset-only (remove seen ID tracking)
- [ ] Add cache invalidation on media updates

### Phase 5: Observability
- [ ] Add metrics: cache hit rate, band fetch count, avg latency
- [ ] Add logging: band fetch, cache warm-up, eviction events
- [ ] Add health check: cache size, memory usage
- [ ] Dashboard: scroll depth histogram, cache effectiveness

---

## Future Enhancements

### Short-term (Next 3 months)
- **Cluster-based pre-warming:** Pre-fetch bands for popular queries
- **Compressed cache:** Store only IDs + scores, lazy-load full entities
- **Redis-backed cache:** Share cache across API instances

### Long-term (6+ months)
- **Cursor-based pagination:** If Weaviate adds HNSW continuation support
- **MMR diversity:** Maximal Marginal Relevance for variety across pages
- **Hierarchical browsing:** Cluster/topic-based navigation instead of flat scroll

---

## References

- [Weaviate Vector Search Documentation](https://weaviate.io/developers/weaviate/search/similarity)
- [Redis Sorted Sets for Pagination](https://redis.io/commands/zrange/)
- [Maximal Marginal Relevance (MMR)](https://www.cs.cmu.edu/~jgc/publication/The_Use_MMR_Diversity_Based_LTMIR_1998.pdf)
- ADR-0051: Vector Hybrid Search with BM25 Fusion
- ADR-0050: Data Access Pagination and Streaming

---

## Decision Log

**2025-01-04:** Initial proposal and architecture design
**2025-01-04:** Accepted after review - Adaptive widening added for guaranteed page sizes
