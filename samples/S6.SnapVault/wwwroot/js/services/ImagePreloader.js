/**
 * Image Preloader with LRU Eviction
 * Intelligently preloads images based on navigation patterns
 *
 * 5-Tier Preloading Strategy:
 * - Tier 1 (Immediate): Current photo - full resolution
 * - Tier 2 (Critical): ±1 photos - preload immediately
 * - Tier 3 (High): ±2-5 photos - preload with priority
 * - Tier 4 (Background): ±6-20 photos - gallery tier thumbnails
 * - Tier 5 (Metadata): ±21-100 photos - metadata only (no images)
 */

export class ImagePreloader {
  constructor(config = {}) {
    this.maxCachedImages = config.maxCachedImages || 20;
    this.tier2Range = config.tier2Range || 1;      // ±1
    this.tier3Range = config.tier3Range || 5;      // ±2-5
    this.tier4Range = config.tier4Range || 20;     // ±6-20

    // Cache storage: Map<photoId, { image, url, timestamp, priority }>
    this.imageCache = new Map();

    // Preload queue with priorities
    this.preloadQueue = [];
    this.isPreloading = false;

    // Velocity tracking for adaptive preloading
    this.navigationHistory = [];
    this.velocityWindow = 5;  // Track last 5 navigations

    // Stats
    this.preloadsExecuted = 0;
    this.preloadsUsed = 0;
    this.totalLoadTime = 0;
  }

  /**
   * Preload images around a position with intelligent prioritization
   */
  async preload(photoSet, currentIndex) {
    // Detect navigation velocity
    const velocity = this.calculateVelocity();

    // Adaptive range based on velocity
    let aheadBias = 1;  // Default: equal ahead/behind
    if (velocity > 0.3) {  // Rapid forward navigation
      aheadBias = 2;  // Preload 2x more ahead
    } else if (velocity < -0.3) {  // Rapid backward navigation
      aheadBias = 0.5;  // Preload 2x more behind
    }

    // Build preload plan
    const plan = this.buildPreloadPlan(
      currentIndex,
      photoSet.totalCount,
      aheadBias
    );

    // Execute preload plan
    await this.executePreloadPlan(plan, photoSet);
  }

  /**
   * Build a prioritized preload plan
   */
  buildPreloadPlan(currentIndex, totalCount, aheadBias = 1) {
    const plan = [];

    // Tier 2: Critical (±1) - highest priority
    for (let offset = -this.tier2Range; offset <= this.tier2Range; offset++) {
      if (offset === 0) continue;  // Skip current (already loading)

      const index = currentIndex + offset;
      if (index >= 0 && index < totalCount) {
        plan.push({
          index,
          priority: 'critical',
          quality: 'original',
          delay: 50
        });
      }
    }

    // Tier 3: High (±2-5) - high priority
    const tier3Start = this.tier2Range + 1;
    for (let offset = -this.tier3Range; offset <= this.tier3Range; offset++) {
      if (Math.abs(offset) <= this.tier2Range) continue;

      const index = currentIndex + offset;
      if (index >= 0 && index < totalCount) {
        // Apply ahead bias
        const biasedDelay = offset > 0
          ? 200 / aheadBias
          : 200 * aheadBias;

        plan.push({
          index,
          priority: 'high',
          quality: 'original',
          delay: biasedDelay
        });
      }
    }

    // Tier 4: Background (±6-20) - low priority, thumbnails only
    const tier4Start = this.tier3Range + 1;
    for (let offset = -this.tier4Range; offset <= this.tier4Range; offset++) {
      if (Math.abs(offset) <= this.tier3Range) continue;

      const index = currentIndex + offset;
      if (index >= 0 && index < totalCount) {
        plan.push({
          index,
          priority: 'low',
          quality: 'gallery',  // Thumbnail quality
          delay: 500
        });
      }
    }

    return plan;
  }

  /**
   * Execute preload plan asynchronously
   */
  async executePreloadPlan(plan, photoSet) {
    // Sort by priority (critical first, then high, then low)
    const priorityOrder = { critical: 0, high: 1, low: 2 };
    plan.sort((a, b) => priorityOrder[a.priority] - priorityOrder[b.priority]);

    // Queue preloads
    for (const item of plan) {
      this.queuePreload(item, photoSet);
    }

    // Start processing queue if not already running
    if (!this.isPreloading) {
      this.processQueue();
    }
  }

  /**
   * Queue a preload task
   */
  queuePreload(task, photoSet) {
    // Check if already in cache
    const photo = photoSet.window.get(task.index);
    if (!photo) return;  // Metadata not loaded yet

    if (this.imageCache.has(photo.id)) {
      return;  // Already cached
    }

    // Check if already queued
    if (this.preloadQueue.some(t => t.index === task.index)) {
      return;  // Already queued
    }

    this.preloadQueue.push({ ...task, photoSet, photo });
  }

  /**
   * Process preload queue
   */
  async processQueue() {
    this.isPreloading = true;

    while (this.preloadQueue.length > 0) {
      const task = this.preloadQueue.shift();

      // Wait for delay
      if (task.delay > 0) {
        await new Promise(resolve => setTimeout(resolve, task.delay));
      }

      // Preload image
      await this.preloadImage(task.photo.id, task.quality);
    }

    this.isPreloading = false;
  }

  /**
   * Preload a single image
   */
  async preloadImage(photoId, quality = 'original') {
    const startTime = performance.now();

    try {
      const url = quality === 'original'
        ? `/api/media/photos/${photoId}/original`
        : `/api/media/photos/${photoId}/gallery`;

      // Create Image object to preload
      const img = new Image();
      img.decoding = 'async';

      const loadPromise = new Promise((resolve, reject) => {
        img.onload = resolve;
        img.onerror = reject;
      });

      img.src = url;
      await loadPromise;

      // Store in cache
      this.imageCache.set(photoId, {
        image: img,
        url: url,
        timestamp: Date.now(),
        priority: quality === 'original' ? 1 : 2,
        quality: quality
      });

      this.preloadsExecuted++;
      const loadTime = performance.now() - startTime;
      this.totalLoadTime += loadTime;

      console.log(`[ImagePreloader] Preloaded ${photoId} (${quality}) in ${loadTime.toFixed(0)}ms`);

      // Evict if cache is full
      if (this.imageCache.size > this.maxCachedImages) {
        this.evictLRU();
      }

    } catch (error) {
      console.error(`[ImagePreloader] Failed to preload ${photoId}:`, error);
    }
  }

  /**
   * Get preloaded image from cache
   */
  getCached(photoId) {
    const cached = this.imageCache.get(photoId);
    if (cached) {
      // Update timestamp (LRU tracking)
      cached.timestamp = Date.now();
      this.preloadsUsed++;
      return cached;
    }
    return null;
  }

  /**
   * Evict least recently used image
   */
  evictLRU() {
    let lruId = null;
    let lruTimestamp = Infinity;

    // Find least recently used
    for (const [photoId, cached] of this.imageCache) {
      if (cached.timestamp < lruTimestamp) {
        lruTimestamp = cached.timestamp;
        lruId = photoId;
      }
    }

    if (lruId) {
      console.log(`[ImagePreloader] Evicting LRU image: ${lruId}`);

      const cached = this.imageCache.get(lruId);

      // Revoke object URL if present
      if (cached.url && cached.url.startsWith('blob:')) {
        URL.revokeObjectURL(cached.url);
      }

      this.imageCache.delete(lruId);
    }
  }

  /**
   * Calculate navigation velocity
   * Returns: -1.0 (fast backward) to +1.0 (fast forward)
   */
  calculateVelocity() {
    if (this.navigationHistory.length < 2) {
      return 0;
    }

    // Take last N navigations
    const recent = this.navigationHistory.slice(-this.velocityWindow);

    // Calculate average direction and speed
    let totalDelta = 0;
    let totalTime = 0;

    for (let i = 1; i < recent.length; i++) {
      const delta = recent[i].index - recent[i - 1].index;
      const timeDelta = recent[i].timestamp - recent[i - 1].timestamp;

      totalDelta += delta;
      totalTime += timeDelta;
    }

    // Velocity: photos per second, normalized to [-1, 1]
    const photosPerSecond = (totalDelta / totalTime) * 1000;
    const normalized = Math.max(-1, Math.min(1, photosPerSecond / 5));

    return normalized;
  }

  /**
   * Record navigation for velocity tracking
   */
  recordNavigation(index) {
    this.navigationHistory.push({
      index,
      timestamp: Date.now()
    });

    // Keep only recent history
    if (this.navigationHistory.length > this.velocityWindow * 2) {
      this.navigationHistory.shift();
    }
  }

  /**
   * Clear all cached images
   */
  clear() {
    console.log(`[ImagePreloader] Clearing cache (${this.imageCache.size} images)`);

    // Revoke all object URLs
    for (const [photoId, cached] of this.imageCache) {
      if (cached.url && cached.url.startsWith('blob:')) {
        URL.revokeObjectURL(cached.url);
      }
    }

    this.imageCache.clear();
    this.preloadQueue = [];
  }

  /**
   * Get cache statistics
   */
  get stats() {
    const efficiency = this.preloadsExecuted > 0
      ? this.preloadsUsed / this.preloadsExecuted
      : 0;

    const avgLoadTime = this.preloadsExecuted > 0
      ? this.totalLoadTime / this.preloadsExecuted
      : 0;

    return {
      cachedImages: this.imageCache.size,
      maxImages: this.maxCachedImages,
      preloadsExecuted: this.preloadsExecuted,
      preloadsUsed: this.preloadsUsed,
      efficiency: efficiency,
      avgLoadTime: avgLoadTime,
      queueLength: this.preloadQueue.length,
      velocity: this.calculateVelocity()
    };
  }
}
