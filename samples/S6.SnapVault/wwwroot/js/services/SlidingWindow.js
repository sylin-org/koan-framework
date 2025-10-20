/**
 * Sliding Window Cache Controller
 * Manages a sliding window of photo metadata for efficient navigation
 *
 * Maintains a fixed-size cache that "slides" as the user navigates,
 * keeping relevant data in memory while evicting data that's no longer needed.
 */

export class SlidingWindow {
  constructor(config = {}) {
    this.windowSize = config.windowSize || 200;
    this.centerOffset = config.centerOffset || 100;
    this.preloadThreshold = config.preloadThreshold || 20;
    this.maxRetries = config.maxRetries || 3;
    this.retryDelay = config.retryDelay || 1000;

    // Cache storage: Map<index, photoMetadata>
    this.cache = new Map();

    // Current window boundaries
    this.start = 0;
    this.end = 0;

    // Stats
    this.hits = 0;
    this.misses = 0;
    this.slides = 0;
  }

  /**
   * Check if a photo at the given index is in cache
   */
  has(index) {
    const inCache = this.cache.has(index);
    if (inCache) {
      this.hits++;
    } else {
      this.misses++;
    }
    return inCache;
  }

  /**
   * Get photo metadata from cache
   */
  get(index) {
    return this.cache.get(index);
  }

  /**
   * Add photo metadata to cache
   */
  set(index, metadata) {
    this.cache.set(index, metadata);
  }

  /**
   * Get multiple photos from cache
   */
  getRange(startIndex, count) {
    const photos = [];
    for (let i = startIndex; i < startIndex + count; i++) {
      if (this.cache.has(i)) {
        photos.push({ index: i, data: this.cache.get(i) });
      }
    }
    return photos;
  }

  /**
   * Add multiple photos to cache
   */
  setRange(startIndex, photos) {
    photos.forEach((photo, offset) => {
      this.cache.set(startIndex + offset, photo);
    });
  }

  /**
   * Check if window needs to slide based on current position
   */
  needsSlide(currentIndex) {
    // Need to slide if current position is too close to window edges
    return (
      currentIndex < this.start + this.preloadThreshold ||
      currentIndex > this.end - this.preloadThreshold
    );
  }

  /**
   * Calculate new window range centered on current position
   */
  calculateNewRange(currentIndex, totalCount) {
    // Calculate ideal window boundaries
    let newStart = currentIndex - this.centerOffset;
    let newEnd = currentIndex + (this.windowSize - this.centerOffset);

    // Clamp to collection boundaries
    if (newStart < 0) {
      newStart = 0;
      newEnd = Math.min(this.windowSize, totalCount);
    } else if (newEnd > totalCount) {
      newEnd = totalCount;
      newStart = Math.max(0, totalCount - this.windowSize);
    }

    return { start: newStart, end: newEnd };
  }

  /**
   * Update window boundaries and evict old data
   */
  slide(newStart, newEnd) {
    console.log(`[SlidingWindow] Sliding from [${this.start}, ${this.end}] to [${newStart}, ${newEnd}]`);

    const oldStart = this.start;
    const oldEnd = this.end;

    this.start = newStart;
    this.end = newEnd;
    this.slides++;

    // Evict data outside new window
    this.evictOutsideRange(newStart, newEnd);

    // Calculate what data we're missing in the new range
    return this.getMissingRanges(newStart, newEnd);
  }

  /**
   * Evict cached data outside the current window
   */
  evictOutsideRange(start, end) {
    const toEvict = [];

    for (const [index] of this.cache) {
      if (index < start || index >= end) {
        toEvict.push(index);
      }
    }

    console.log(`[SlidingWindow] Evicting ${toEvict.length} items outside range [${start}, ${end}]`);

    toEvict.forEach(index => this.cache.delete(index));
  }

  /**
   * Determine which ranges within [start, end] are missing from cache
   * Returns an array of { start, end } ranges that need to be fetched
   */
  getMissingRanges(start, end) {
    const missing = [];
    let rangeStart = null;

    for (let i = start; i < end; i++) {
      if (!this.cache.has(i)) {
        // Start a new missing range
        if (rangeStart === null) {
          rangeStart = i;
        }
      } else {
        // End the current missing range
        if (rangeStart !== null) {
          missing.push({ start: rangeStart, end: i });
          rangeStart = null;
        }
      }
    }

    // Close any open range
    if (rangeStart !== null) {
      missing.push({ start: rangeStart, end: end });
    }

    return missing;
  }

  /**
   * Clear all cached data
   */
  clear() {
    console.log(`[SlidingWindow] Clearing cache (${this.cache.size} items)`);
    this.cache.clear();
    this.start = 0;
    this.end = 0;
  }

  /**
   * Get cache statistics
   */
  get stats() {
    const hitRate = this.hits / (this.hits + this.misses) || 0;

    return {
      size: this.cache.size,
      windowSize: this.windowSize,
      hits: this.hits,
      misses: this.misses,
      hitRate: hitRate,
      slides: this.slides,
      range: { start: this.start, end: this.end }
    };
  }

  /**
   * Reset statistics
   */
  resetStats() {
    this.hits = 0;
    this.misses = 0;
    this.slides = 0;
  }
}
