# PhotoSet Navigation System - Technical Specification

**Version**: 1.0
**Date**: 2025-10-19
**Status**: Approved - Implementation Phase
**Author**: SnapVault Engineering Team

---

## Executive Summary

Transform SnapVault's lightbox navigation from bounded (limited to loaded photos) to unbounded (entire collection accessible) using a sliding window architecture with intelligent preloading.

**Problem**: Users can only navigate through currently loaded photos (~100), creating artificial barriers when browsing large collections (1,000s–100,000s of photos).

**Solution**: PhotoSet Manager with sliding window cache + aggressive preloading enables seamless navigation across entire collections while maintaining <100MB memory footprint.

**Impact**: Navigate 100,000+ photo collections with <50ms latency and >95% cache hit rate.

---

## Architecture Overview

### Three-Layer System

```
┌─────────────────────────────────────────────┐
│ Layer 1: Server (Complete Collection)       │  10,000 photos
│ - Full photo database                       │
│ - Supports range queries with cursor        │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│ Layer 2: Metadata Cache (Sliding Window)    │  200 photos (~200KB)
│ - PhotoSet Manager                          │
│ - SlidingWindow controller                  │
│ - Lightweight metadata only                 │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│ Layer 3: Image Preload (Hot Cache)         │  20 photos (~60MB)
│ - ImagePreloader with LRU eviction         │
│ - Full resolution images                    │
│ - Progressive loading (gallery → original)  │
└─────────────────────────────────────────────┘
                    ↓
              Lightbox Display
```

---

## Core Components

### 1. PhotoSet Manager

**Location**: `/wwwroot/js/services/PhotoSetManager.js`

**Responsibilities**:
- Manage photo collection context (type, filters, sort order)
- Maintain sliding window of metadata
- Coordinate preloading strategy
- Handle navigation (next, previous, jump)
- Provide reactivity through events

**API**:
```javascript
class PhotoSet {
  // Initialization
  constructor(definition)
  async initialize(startPhotoId)

  // Navigation
  async next()
  async previous()
  async jumpTo(index)

  // Data access
  async getPhoto(index)
  async getPhotoById(id)
  async getRange(startIndex, count)

  // State queries
  get currentIndex
  get totalCount
  get canGoNext
  get canGoPrevious
  get progress  // { current, total, percentage }

  // Events
  on(event, handler)  // 'navigate', 'load', 'error', 'invalidate'
  off(event, handler)
}
```

**Set Definition Schema**:
```typescript
interface SetDefinition {
  type: 'all-photos' | 'collection' | 'favorites' | 'search';
  id?: string;  // Collection ID if type='collection'
  filters?: {
    rating?: { min: number; max: number };
    dateRange?: { start: Date; end: Date };
    tags?: string[];
  };
  sortBy: 'capturedAt' | 'createdAt' | 'rating' | 'fileName';
  sortOrder: 'asc' | 'desc';
  searchQuery?: string;  // If type='search'
}
```

### 2. Sliding Window Controller

**Location**: `/wwwroot/js/services/SlidingWindow.js`

**Responsibilities**:
- Maintain cache of N photos around current position
- Detect when window needs to slide
- Request data from server when sliding
- Evict old data outside window

**Configuration**:
```javascript
{
  windowSize: 200,        // Total photos to keep in memory
  centerOffset: 100,      // Keep current photo at this position
  preloadThreshold: 20,   // Slide when within this many of edge
  maxRetries: 3,          // Retry failed loads
  retryDelay: 1000        // ms between retries
}
```

**Algorithm**:
```javascript
// When currentIndex changes
if (currentIndex < window.start + preloadThreshold ||
    currentIndex > window.end - preloadThreshold) {
  // Calculate new window range
  newStart = currentIndex - centerOffset
  newEnd = currentIndex + (windowSize - centerOffset)

  // Fetch missing data
  await fetchRange(newStart, newEnd)

  // Evict old data
  evictOutsideRange(newStart, newEnd)
}
```

### 3. Image Preloader

**Location**: `/wwwroot/js/services/ImagePreloader.js`

**Responsibilities**:
- Preload images based on navigation likelihood
- Manage memory with LRU eviction
- Support velocity-adaptive preloading
- Progressive loading (gallery → original)

**5-Tier Preload Strategy**:

| Tier | Range | Quality | Priority | Delay |
|------|-------|---------|----------|-------|
| 1 - Immediate | Current | Original | Critical | 0ms |
| 2 - Critical | ±1 | Original | High | 50ms |
| 3 - High | ±2-5 | Original | Medium | 200ms |
| 4 - Background | ±6-20 | Gallery | Low | 500ms |
| 5 - Metadata | ±21-100 | None | Lowest | As needed |

**Velocity Adaptation**:
```javascript
// Detect rapid navigation
if (avgNavigationInterval < 200ms) {
  // User browsing quickly - load more ahead
  preloadAhead += 5;
  preloadBehind -= 3;
} else {
  // Normal browsing - balanced
  preloadAhead = preloadBehind = 5;
}
```

**Memory Management**:
```javascript
const MAX_CACHED_IMAGES = 20;  // ~60MB at 3MB/image
const evictionPolicy = 'LRU';  // Least Recently Used

// When cache full
if (cache.size >= MAX_CACHED_IMAGES) {
  const lru = cache.getLRU();
  cache.evict(lru);
  URL.revokeObjectURL(lru.url);  // Free memory
}
```

---

## Backend API Specification

### New Endpoints

#### 1. Get Photo Index in Context

```http
GET /api/photos/{id}/index

Query Parameters:
  context: string           # 'all-photos' | 'collection' | 'favorites' | 'search'
  collectionId?: string     # Required if context='collection'
  sortBy: string           # 'capturedAt' | 'createdAt' | 'rating' | 'fileName'
  sortOrder: string        # 'asc' | 'desc'
  filters?: string         # JSON-encoded filter object

Response: 200 OK
{
  "index": 2453,
  "totalCount": 10000,
  "hasNext": true,
  "hasPrevious": true
}
```

**Implementation** (C#):
```csharp
[HttpGet("{id}/index")]
public async Task<ActionResult<PhotoIndexResponse>> GetPhotoIndex(
    string id,
    [FromQuery] string context,
    [FromQuery] string? collectionId,
    [FromQuery] string sortBy = "capturedAt",
    [FromQuery] string sortOrder = "desc",
    [FromQuery] string? filters = null)
{
    var query = BuildPhotoQuery(context, collectionId, sortBy, sortOrder, filters);

    // Use window function for efficient index calculation
    var result = await query
        .Select((photo, index) => new { photo.Id, Index = index })
        .FirstOrDefaultAsync(x => x.Id == id);

    if (result == null)
        return NotFound();

    var totalCount = await query.CountAsync();

    return new PhotoIndexResponse
    {
        Index = result.Index,
        TotalCount = totalCount,
        HasNext = result.Index < totalCount - 1,
        HasPrevious = result.Index > 0
    };
}
```

#### 2. Get Range of Photos

```http
GET /api/photos/range

Query Parameters:
  context: string           # Same as above
  collectionId?: string
  startIndex: number       # 0-based index
  count: number            # Number of photos to fetch
  sortBy: string
  sortOrder: string
  filters?: string
  includeMetadata: boolean  # Default: false (lightweight response)

Response: 200 OK
{
  "photos": [
    {
      "id": "abc123",
      "fileName": "sunset.jpg",
      "capturedAt": "2024-08-15T19:30:00Z",
      "thumbnailUrl": "/api/media/photos/abc123/thumbnail",
      "rating": 4,
      "isFavorite": false
    },
    // ... more photos
  ],
  "startIndex": 2353,
  "count": 200,
  "totalCount": 10000
}
```

**Implementation** (C#):
```csharp
[HttpGet("range")]
public async Task<ActionResult<PhotoRangeResponse>> GetPhotoRange(
    [FromQuery] string context,
    [FromQuery] string? collectionId,
    [FromQuery] int startIndex,
    [FromQuery] int count = 100,
    [FromQuery] string sortBy = "capturedAt",
    [FromQuery] string sortOrder = "desc",
    [FromQuery] string? filters = null,
    [FromQuery] bool includeMetadata = false)
{
    if (count > 200)
        count = 200;  // Limit max batch size

    var query = BuildPhotoQuery(context, collectionId, sortBy, sortOrder, filters);

    var photos = await query
        .Skip(startIndex)
        .Take(count)
        .Select(p => new PhotoMetadata
        {
            Id = p.Id,
            FileName = p.OriginalFileName,
            CapturedAt = p.CapturedAt,
            ThumbnailUrl = $"/api/media/photos/{p.Id}/thumbnail",
            Rating = p.Rating,
            IsFavorite = p.IsFavorite
        })
        .ToListAsync();

    return new PhotoRangeResponse
    {
        Photos = photos,
        StartIndex = startIndex,
        Count = photos.Count,
        TotalCount = await query.CountAsync()
    };
}
```

#### 3. Get Adjacent Photo

```http
GET /api/photos/{id}/adjacent

Query Parameters:
  context: string
  collectionId?: string
  direction: string        # 'next' | 'previous'
  distance: number         # Default: 1 (immediate neighbor)
  sortBy: string
  sortOrder: string
  filters?: string

Response: 200 OK
{
  "id": "def456",
  "fileName": "morning-coffee.jpg",
  "index": 2454,
  "galleryUrl": "/api/media/photos/def456/gallery",
  "originalUrl": "/api/media/photos/def456/original",
  // ... full photo metadata
}

Response: 404 Not Found (if at boundary)
{
  "error": "No adjacent photo found",
  "reason": "at_end_of_set"
}
```

**Implementation** (C#):
```csharp
[HttpGet("{id}/adjacent")]
public async Task<ActionResult<Photo>> GetAdjacentPhoto(
    string id,
    [FromQuery] string context,
    [FromQuery] string? collectionId,
    [FromQuery] string direction,
    [FromQuery] int distance = 1,
    [FromQuery] string sortBy = "capturedAt",
    [FromQuery] string sortOrder = "desc",
    [FromQuery] string? filters = null)
{
    var query = BuildPhotoQuery(context, collectionId, sortBy, sortOrder, filters);

    // Find current photo's index
    var currentIndex = await query
        .Select((photo, index) => new { photo.Id, Index = index })
        .Where(x => x.Id == id)
        .Select(x => x.Index)
        .FirstOrDefaultAsync();

    var targetIndex = direction == "next"
        ? currentIndex + distance
        : currentIndex - distance;

    if (targetIndex < 0 || targetIndex >= await query.CountAsync())
        return NotFound(new { error = "No adjacent photo found", reason = "at_end_of_set" });

    var adjacentPhoto = await query
        .Skip(targetIndex)
        .Take(1)
        .FirstOrDefaultAsync();

    return Ok(adjacentPhoto);
}
```

### Database Optimization

**Required Indexes**:
```sql
CREATE INDEX IX_Photos_CapturedAt ON Photos(CapturedAt DESC);
CREATE INDEX IX_Photos_CreatedAt ON Photos(CreatedAt DESC);
CREATE INDEX IX_Photos_Rating ON Photos(Rating DESC);
CREATE INDEX IX_Photos_FileName ON Photos(OriginalFileName);
CREATE INDEX IX_Photos_IsFavorite ON Photos(IsFavorite) WHERE IsFavorite = 1;
CREATE INDEX IX_CollectionPhotos_CollectionId_Order ON CollectionPhotos(CollectionId, OrderIndex);
```

**Query Optimization**:
- Use `ROW_NUMBER()` window function for efficient indexing
- Cursor-based pagination for collections > 10K photos
- Response compression (gzip/brotli)
- ETag caching for unchanged ranges

---

## Frontend Integration

### Lightbox Integration

**Location**: `/wwwroot/js/components/lightbox.js`

**Changes**:
```javascript
// OLD: Bounded by app.photos array
this.currentIndex = this.app.photos.findIndex(p => p.id === photoId);

async next() {
  if (this.currentIndex < this.app.photos.length - 1) {
    // Can only navigate within loaded photos
  }
}

// NEW: Unbounded navigation via PhotoSet
this.photoSet = null;  // Initialized when lightbox opens

async open(photoId) {
  // Create PhotoSet from current view context
  const definition = this.app.components.collectionView.getSetDefinition();
  this.photoSet = new PhotoSet(definition);
  await this.photoSet.initialize(photoId);

  this.currentIndex = this.photoSet.currentIndex;
  this.totalCount = this.photoSet.totalCount;

  // ... rest of open logic
}

async next() {
  if (this.photoSet.canGoNext) {
    await this.photoSet.next();
    this.currentIndex = this.photoSet.currentIndex;
    this.currentPhoto = await this.photoSet.getCurrentPhoto();
    await this.loadPhoto();
    this.updateMetadata();
    this.updateNavigation();
    this.updatePositionIndicator();
  }
}

async previous() {
  if (this.photoSet.canGoPrevious) {
    await this.photoSet.previous();
    // ... same as next
  }
}
```

### CollectionView Integration

**New Method**:
```javascript
// CollectionView.js
getSetDefinition() {
  // Convert viewState to PhotoSet definition
  return {
    type: this.viewState.type,
    id: this.viewState.collection?.id,
    filters: this.getActiveFilters(),
    sortBy: this.getSortBy(),
    sortOrder: this.getSortOrder(),
    searchQuery: this.getSearchQuery()
  };
}
```

---

## UI Enhancements

### Position Indicator

**Location**: Lightbox top bar, right side

**HTML**:
```html
<div class="lightbox-position-indicator">
  <span class="position-current">2,453</span>
  <span class="position-separator">/</span>
  <span class="position-total">10,000</span>
</div>
```

**CSS**:
```css
.lightbox-position-indicator {
  display: flex;
  align-items: baseline;
  gap: 4px;
  font-size: 14px;
  color: rgba(255, 255, 255, 0.7);
  font-variant-numeric: tabular-nums;
}

.position-current {
  font-weight: 600;
  color: rgba(255, 255, 255, 0.9);
}

.position-separator {
  color: rgba(255, 255, 255, 0.4);
}
```

### Loading States

**Optimistic UI**:
```javascript
async next() {
  // 1. Show cached thumbnail immediately (0ms)
  const thumbnail = this.photoSet.getCachedThumbnail(this.currentIndex + 1);
  if (thumbnail) {
    this.showThumbnail(thumbnail);
  }

  // 2. Show loading indicator if >100ms
  const loadingTimeout = setTimeout(() => {
    this.showLoadingState();
  }, 100);

  // 3. Load full photo
  await this.photoSet.next();
  clearTimeout(loadingTimeout);

  // 4. Display full quality
  await this.loadPhoto();
}
```

**Loading Indicator**:
```html
<div class="lightbox-loading-overlay" hidden>
  <div class="loading-spinner"></div>
  <div class="loading-text">Loading photo...</div>
</div>
```

### Keyboard Shortcuts

**New Shortcuts**:

| Shortcut | Action |
|----------|--------|
| `Home` | Jump to first photo |
| `End` | Jump to last photo |
| `Shift + →` | Jump +10 photos |
| `Shift + ←` | Jump -10 photos |
| `Ctrl/Cmd + →` | Jump to end |
| `Ctrl/Cmd + ←` | Jump to start |

**Implementation**:
```javascript
// lightboxKeyboard.js
if (e.key === 'Home') {
  await this.lightbox.photoSet.jumpTo(0);
} else if (e.key === 'End') {
  await this.lightbox.photoSet.jumpTo(this.lightbox.photoSet.totalCount - 1);
} else if (e.shiftKey && e.key === 'ArrowRight') {
  const target = Math.min(
    this.lightbox.currentIndex + 10,
    this.lightbox.photoSet.totalCount - 1
  );
  await this.lightbox.photoSet.jumpTo(target);
}
```

---

## Performance Targets

| Metric | Target | Measurement |
|--------|--------|-------------|
| Navigation latency (cached) | <50ms | Performance.now() |
| Navigation latency (uncached) | <500ms | Performance.now() |
| Initial load time | <300ms | Lighthouse |
| Cache hit rate | >95% | PhotoSet.metrics |
| Memory usage (100K photos) | <100MB | performance.memory |
| Window slide time | <100ms | Background |
| API response time (range) | <200ms | Server logs |

**Monitoring**:
```javascript
class PhotoSet {
  get metrics() {
    return {
      cacheHitRate: this.cacheHits / (this.cacheHits + this.cacheMisses),
      avgNavigationTime: this.totalNavigationTime / this.navigationCount,
      memoryUsage: performance.memory?.usedJSHeapSize / 1024 / 1024,
      preloadEfficiency: this.preloadsUsed / this.preloadsExecuted
    };
  }
}
```

---

## Error Handling

### Scenarios & Solutions

**1. Photo Deleted During Navigation**
```javascript
try {
  await photoSet.next();
} catch (error) {
  if (error.code === 'PHOTO_NOT_FOUND') {
    // Auto-skip to next available photo
    await photoSet.next();  // Recursive retry
  }
}
```

**2. Network Failure**
```javascript
if (!navigator.onLine) {
  // Show offline message
  toast.show('Offline - showing cached photos only');
  // Limit navigation to cached range
  photoSet.setOfflineMode(true);
}
```

**3. Timeout (Slow Network)**
```javascript
const timeout = setTimeout(() => {
  toast.show('Taking longer than expected...', {
    actions: [
      { label: 'Retry', action: () => retry() },
      { label: 'Skip', action: () => skip() }
    ]
  });
}, 5000);
```

**4. Set Invalidation**
```javascript
// User modifies photo in another tab
broadcastChannel.on('photo-updated', ({ photoId }) => {
  if (photoSet.currentPhoto?.id === photoId) {
    // Refresh current photo
    await photoSet.refreshCurrent();
  }
  // Invalidate cache entry
  photoSet.invalidateCache(photoId);
});
```

---

## Testing Strategy

### Unit Tests

**PhotoSet Manager**:
```javascript
describe('PhotoSet', () => {
  test('initializes with correct index', async () => {
    const set = new PhotoSet(definition);
    await set.initialize('photo-500');
    expect(set.currentIndex).toBe(500);
  });

  test('navigates forward within bounds', async () => {
    await set.next();
    expect(set.currentIndex).toBe(501);
  });

  test('prevents navigation past boundary', async () => {
    await set.jumpTo(9999);
    await set.next();
    expect(set.currentIndex).toBe(9999);
  });

  test('maintains cache within window', async () => {
    await set.jumpTo(5000);
    expect(set.cache.size).toBeLessThanOrEqual(200);
  });
});
```

**Sliding Window**:
```javascript
describe('SlidingWindow', () => {
  test('slides when approaching edge', () => {
    window.setPosition(190);  // Near end of 200-item window
    expect(window.needsSlide()).toBe(true);
  });

  test('evicts old data', () => {
    window.slide(100, 300);
    expect(window.has(50)).toBe(false);  // Outside range
  });
});
```

### Integration Tests

**Lightbox Navigation**:
```javascript
test('navigates through entire collection', async () => {
  const lightbox = new Lightbox(app);
  await lightbox.open('photo-1');

  // Navigate 100 times
  for (let i = 0; i < 100; i++) {
    await lightbox.next();
  }

  expect(lightbox.currentIndex).toBe(100);
  expect(lightbox.photoSet.metrics.cacheHitRate).toBeGreaterThan(0.9);
});
```

### Performance Tests

```javascript
test('maintains <50ms navigation latency', async () => {
  const latencies = [];

  for (let i = 0; i < 50; i++) {
    const start = performance.now();
    await photoSet.next();
    latencies.push(performance.now() - start);
  }

  const avg = latencies.reduce((a, b) => a + b) / latencies.length;
  expect(avg).toBeLessThan(50);
});
```

---

## Implementation Roadmap

### Phase 1: Core Functionality (Week 1-2)
- [ ] Create PhotoSet class
- [ ] Implement SlidingWindow controller
- [ ] Add API endpoints (index, range, adjacent)
- [ ] Integrate with Lightbox
- [ ] Add position indicator UI
- [ ] Basic preloading (±1 photos)

**Success Criteria**: Navigate 10K+ photos seamlessly

### Phase 2: Performance Optimization (Week 3)
- [ ] Implement 5-tier preload strategy
- [ ] Add velocity-adaptive preloading
- [ ] Memory management & LRU eviction
- [ ] Progressive image loading (blur-up)
- [ ] Loading state improvements

**Success Criteria**: <50ms navigation, >95% cache hit rate

### Phase 3: Advanced Features (Week 4)
- [ ] Filmstrip scrubber (G key)
- [ ] Jump shortcuts (Shift/Ctrl + arrows, Home/End)
- [ ] Navigation history (back/forward)
- [ ] Boundary smart suggestions

**Success Criteria**: Power user navigation complete

### Phase 4: Production Polish (Week 5)
- [ ] Error handling (deleted photos, network issues)
- [ ] Multi-tab sync (BroadcastChannel)
- [ ] Performance monitoring dashboard
- [ ] Accessibility audit
- [ ] Mobile optimization

**Success Criteria**: Production-ready quality

---

## Success Metrics

### Quantitative

- **Navigation Latency**: <50ms (cached), <500ms (uncached)
- **Cache Hit Rate**: >95%
- **Memory Usage**: <100MB for 100K photo set
- **Max Navigable Photos**: 100,000+
- **User Navigation Distance**: Average 50+ photos per session (up from ~10)

### Qualitative

- User quotes: "I can finally browse my entire library!"
- Reduced support tickets about navigation limits
- Increased session duration in lightbox
- Higher return usage of photo viewing features

---

## Appendix

### Browser Compatibility

- Chrome/Edge: 90+ (ES2020, WeakMap, performance.memory)
- Firefox: 88+
- Safari: 14+
- Mobile Safari: 14+

### Dependencies

- None (vanilla JavaScript)
- Existing: StateRegistry for reactivity
- Existing: API client for HTTP requests

### File Structure

```
/wwwroot/js/services/
  ├── PhotoSetManager.js      (Main coordinator)
  ├── SlidingWindow.js         (Cache controller)
  └── ImagePreloader.js        (Preload strategy)

/Controllers/
  └── PhotosController.cs      (New endpoints)

/Models/
  ├── PhotoIndexResponse.cs
  ├── PhotoRangeResponse.cs
  └── SetDefinition.cs

/wwwroot/js/components/
  └── lightbox.js              (Updated integration)
```

---

**Document End**

*This specification is the authoritative guide for implementing unbounded lightbox navigation in SnapVault. All implementation decisions should reference this document.*
