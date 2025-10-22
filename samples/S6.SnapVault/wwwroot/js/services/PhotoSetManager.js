/**
 * PhotoSet Manager
 * Main coordinator for unbounded photo navigation
 *
 * Manages a virtual "set" of photos (All Photos, Collection, Favorites, etc.)
 * with efficient sliding window caching and intelligent preloading.
 */

import { SlidingWindow } from './SlidingWindow.js';
import { ImagePreloader } from './ImagePreloader.js';

export class PhotoSetManager {
  constructor(definition, api) {
    this.definition = definition;
    this.api = api;

    // Sliding window for metadata
    this.window = new SlidingWindow({
      windowSize: 200,
      centerOffset: 100,
      preloadThreshold: 20
    });

    // Image preloader
    this.preloader = new ImagePreloader({
      maxCachedImages: 20,
      tier2Range: 1,
      tier3Range: 5,
      tier4Range: 20
    });

    // Session state
    this.sessionId = null;      // Server-assigned session ID

    // Current state
    this.currentIndex = -1;
    this.currentPhoto = null;
    this.totalCount = 0;
    this.isInitialized = false;

    // Event listeners
    this.listeners = new Map();

    // Performance metrics
    this.metrics = {
      navigationCount: 0,
      totalNavigationTime: 0,
      cacheHits: 0,
      cacheMisses: 0
    };
  }

  /**
   * Initialize the photo set with a starting photo (for Lightbox)
   */
  async initialize(startPhotoId) {
    console.log('[PhotoSet] Initializing with photo:', startPhotoId);

    try {
      // Create session first to get full photo list
      // Load initial window starting from beginning (creates session)
      const response = await this.loadWindow(0, 200);

      this.totalCount = response.totalCount;

      // Find photo's index in the session
      // First check the loaded window
      let foundIndex = -1;
      for (const [index, photo] of this.window.cache.entries()) {
        if (photo.id === startPhotoId) {
          foundIndex = index;
          break;
        }
      }

      // If not in initial window, need to search or use fallback
      if (foundIndex === -1) {
        console.log('[PhotoSet] Photo not in initial window, using index endpoint');
        const indexData = await this.getPhotoIndex(startPhotoId);
        foundIndex = indexData.index;

        // Load window around the found position
        await this.loadWindow(foundIndex);
      }

      this.currentIndex = foundIndex;
      this.currentPhoto = this.window.get(this.currentIndex);

      // Start preloading images
      await this.preloader.preload(this, this.currentIndex);

      this.isInitialized = true;

      console.log(`[PhotoSet] Initialized at index ${this.currentIndex} of ${this.totalCount}`);

      return true;
    } catch (error) {
      console.error('[PhotoSet] Initialization failed:', error);
      throw error;
    }
  }

  /**
   * Initialize photo set for grid display (no specific photo required)
   * Loads from beginning of set for initial grid rendering
   * Uses cache for instant response, then refreshes in background
   */
  async initializeForGrid(startIndex = 0) {
    console.log('[PhotoSet] Initializing for grid at index:', startIndex);

    try {
      // Build definition for cache lookup
      const cacheDefinition = {
        context: this.definition.type,
        searchQuery: this.definition.searchQuery,
        searchAlpha: this.definition.searchAlpha,
        collectionId: this.definition.id,
        sortBy: this.definition.sortBy || 'capturedAt',
        sortOrder: this.definition.sortOrder || 'desc'
      };

      // Check cache first
      const cached = window.photoSetCache?.get(cacheDefinition);

      if (cached) {
        const cacheAge = Date.now() - cached.timestamp;
        const ageMinutes = Math.round(cacheAge / 1000 / 60);
        const ageText = ageMinutes < 1 ? 'just now' :
                       ageMinutes < 60 ? `${ageMinutes}m ago` :
                       `${Math.round(ageMinutes / 60)}h ago`;

        console.log(`%c[PhotoSet Cache] 📦 LOADED from cache`, 'color: #00a8ff; font-weight: bold', {
          context: cacheDefinition.context,
          photoCount: cached.photos.length,
          cachedAt: new Date(cached.timestamp).toLocaleTimeString(),
          age: ageText,
          sessionId: cached.session.id
        });

        // Store cached session and photos
        this.sessionId = cached.session.id;
        this.totalCount = cached.session.totalCount;
        this.currentIndex = startIndex;
        this.window.setRange(0, cached.photos);
        this.isInitialized = true;

        // Emit event that we're using cached data
        this.emit('cacheLoaded', {
          photoCount: cached.photos.length,
          totalCount: this.totalCount,
          age: cacheAge
        });

        // Start background refresh (non-blocking)
        console.log(`%c[PhotoSet Cache] 🔄 Starting background refresh...`, 'color: #ffa502; font-weight: bold');
        this._refreshInBackground(cacheDefinition, cached).catch(error => {
          console.error('[PhotoSet] Background refresh failed:', error);
        });

        return true;
      }

      // No cache - load fresh data
      console.log(`%c[PhotoSet Cache] ❌ No cache found - loading fresh data`, 'color: #ff6348; font-weight: bold', {
        context: cacheDefinition.context
      });
      const response = await this.loadWindow(startIndex);

      this.totalCount = response.totalCount;
      this.currentIndex = startIndex;
      this.isInitialized = true;

      // Cache the result
      if (window.photoSetCache) {
        const session = {
          id: this.sessionId,
          totalCount: this.totalCount
        };
        window.photoSetCache.set(cacheDefinition, session, response.photos);
        console.log(`%c[PhotoSet Cache] 💾 SAVED to cache`, 'color: #2ecc71; font-weight: bold', {
          context: cacheDefinition.context,
          photoCount: response.photos.length
        });
      }

      console.log(`[PhotoSet] Grid initialized with ${response.photos.length} photos, total: ${this.totalCount}`);

      return true;
    } catch (error) {
      console.error('[PhotoSet] Grid initialization failed:', error);
      throw error;
    }
  }

  /**
   * Refresh photoset data in background and compare with cached version
   */
  async _refreshInBackground(cacheDefinition, cached) {
    console.log('[PhotoSet] Starting background refresh...');

    this.emit('refreshStart', {});

    try {
      // Force fresh load by clearing session ID temporarily
      const oldSessionId = this.sessionId;
      this.sessionId = null;

      const response = await this.loadWindow(0, 200);

      // Restore session ID
      this.sessionId = response.sessionId;

      // Compare with cached data
      const changes = this._detectChanges(cached.photos, response.photos);

      if (changes.hasChanges) {
        console.log(`%c[PhotoSet Cache] 🔄 REPLACED with fresh data`, 'color: #9b59b6; font-weight: bold', {
          context: cacheDefinition.context,
          changes: {
            added: changes.added,
            removed: changes.removed,
            modified: changes.modified
          },
          oldCount: cached.photos.length,
          newCount: response.photos.length
        });

        // Update cache
        const session = {
          id: this.sessionId,
          totalCount: response.totalCount
        };
        window.photoSetCache.set(cacheDefinition, session, response.photos);

        // Update window cache
        this.window.setRange(0, response.photos);
        this.totalCount = response.totalCount;

        // Emit changes event
        this.emit('refreshComplete', {
          changes: changes,
          newTotal: response.totalCount,
          oldTotal: cached.session.totalCount
        });
      } else {
        console.log(`%c[PhotoSet Cache] ✅ Refresh complete - no changes detected`, 'color: #2ecc71; font-weight: bold', {
          context: cacheDefinition.context,
          photoCount: response.photos.length
        });
        this.emit('refreshComplete', { changes: null });
      }

    } catch (error) {
      console.error('[PhotoSet] Background refresh error:', error);
      this.emit('refreshError', { error: error.message });
    }
  }

  /**
   * Detect changes between cached and fresh photo lists
   */
  _detectChanges(cachedPhotos, freshPhotos) {
    const changes = {
      hasChanges: false,
      added: 0,
      removed: 0,
      modified: 0,
      newPhotoIds: []
    };

    // Quick check - total count different
    if (cachedPhotos.length !== freshPhotos.length) {
      changes.hasChanges = true;
      changes.added = Math.max(0, freshPhotos.length - cachedPhotos.length);
      changes.removed = Math.max(0, cachedPhotos.length - freshPhotos.length);
    }

    // Build ID sets for comparison
    const cachedIds = new Set(cachedPhotos.map(p => p.id));
    const freshIds = new Set(freshPhotos.map(p => p.id));

    // Find new photos
    for (const photo of freshPhotos) {
      if (!cachedIds.has(photo.id)) {
        changes.hasChanges = true;
        changes.newPhotoIds.push(photo.id);
      }
    }

    // Check for modified metadata (first 10 photos for performance)
    const checkCount = Math.min(10, cachedPhotos.length, freshPhotos.length);
    for (let i = 0; i < checkCount; i++) {
      if (cachedPhotos[i].id === freshPhotos[i].id) {
        // Same photo - check if metadata changed
        if (this._photoMetadataChanged(cachedPhotos[i], freshPhotos[i])) {
          changes.hasChanges = true;
          changes.modified++;
        }
      }
    }

    return changes;
  }

  /**
   * Check if photo metadata has changed
   */
  _photoMetadataChanged(cached, fresh) {
    return cached.isFavorite !== fresh.isFavorite ||
           cached.rating !== fresh.rating ||
           cached.fileName !== fresh.fileName;
  }

  /**
   * Navigate to next photo
   */
  async next() {
    if (!this.canGoNext) {
      console.warn('[PhotoSet] Already at end of set');
      return false;
    }

    const startTime = performance.now();

    try {
      this.currentIndex++;
      await this.loadPhotoAtIndex(this.currentIndex);

      // Record navigation for velocity tracking
      this.preloader.recordNavigation(this.currentIndex);

      // Update metrics
      this.metrics.navigationCount++;
      this.metrics.navigationTime = performance.now() - startTime;
      this.metrics.totalNavigationTime += this.metrics.navigationTime;

      this.emit('navigate', {
        index: this.currentIndex,
        photo: this.currentPhoto,
        direction: 'next'
      });

      return true;
    } catch (error) {
      console.error('[PhotoSet] Navigation to next failed:', error);
      this.currentIndex--;  // Rollback
      throw error;
    }
  }

  /**
   * Navigate to previous photo
   */
  async previous() {
    if (!this.canGoPrevious) {
      console.warn('[PhotoSet] Already at start of set');
      return false;
    }

    const startTime = performance.now();

    try {
      this.currentIndex--;
      await this.loadPhotoAtIndex(this.currentIndex);

      // Record navigation for velocity tracking
      this.preloader.recordNavigation(this.currentIndex);

      // Update metrics
      this.metrics.navigationCount++;
      this.metrics.navigationTime = performance.now() - startTime;
      this.metrics.totalNavigationTime += this.metrics.navigationTime;

      this.emit('navigate', {
        index: this.currentIndex,
        photo: this.currentPhoto,
        direction: 'previous'
      });

      return true;
    } catch (error) {
      console.error('[PhotoSet] Navigation to previous failed:', error);
      this.currentIndex++;  // Rollback
      throw error;
    }
  }

  /**
   * Jump to specific index
   */
  async jumpTo(targetIndex) {
    if (targetIndex < 0 || targetIndex >= this.totalCount) {
      throw new Error(`Index ${targetIndex} out of bounds [0, ${this.totalCount})`);
    }

    const startTime = performance.now();

    try {
      const oldIndex = this.currentIndex;
      this.currentIndex = targetIndex;

      await this.loadPhotoAtIndex(this.currentIndex);

      // Record navigation
      this.preloader.recordNavigation(this.currentIndex);

      // Update metrics
      this.metrics.navigationCount++;
      this.metrics.navigationTime = performance.now() - startTime;
      this.metrics.totalNavigationTime += this.metrics.navigationTime;

      const direction = targetIndex > oldIndex ? 'next' : 'previous';

      this.emit('navigate', {
        index: this.currentIndex,
        photo: this.currentPhoto,
        direction,
        distance: Math.abs(targetIndex - oldIndex)
      });

      return true;
    } catch (error) {
      console.error('[PhotoSet] Jump to index failed:', error);
      throw error;
    }
  }

  /**
   * Load photo at specific index (internal)
   */
  async loadPhotoAtIndex(index) {
    // Check if we need to slide the window
    if (this.window.needsSlide(index)) {
      await this.slideWindow(index);
    }

    // Get photo from window cache
    let photo = this.window.get(index);

    if (!photo) {
      // Cache miss - fetch single photo
      console.warn(`[PhotoSet] Cache miss at index ${index}, fetching...`);
      this.metrics.cacheMisses++;

      photo = await this.fetchPhotoAtIndex(index);
      this.window.set(index, photo);
    } else {
      this.metrics.cacheHits++;
    }

    this.currentPhoto = photo;

    // Trigger preloading
    await this.preloader.preload(this, this.currentIndex);

    return photo;
  }

  /**
   * Slide the window to a new position
   */
  async slideWindow(targetIndex) {
    const newRange = this.window.calculateNewRange(targetIndex, this.totalCount);

    console.log(`[PhotoSet] Sliding window to center on index ${targetIndex}`);

    // Get missing ranges that need to be fetched
    const missingRanges = this.window.slide(newRange.start, newRange.end);

    // Fetch missing data
    for (const range of missingRanges) {
      await this.loadWindow(range.start, range.end - range.start);
    }
  }

  /**
   * Load a window of photo metadata using session endpoint
   */
  async loadWindow(startIndex, count = null) {
    console.log(`[DEBUG PhotoSet.loadWindow] CALLED with startIndex: ${startIndex}, count: ${count}`);

    if (count === null) {
      // Calculate window size based on current position
      const range = this.window.calculateNewRange(startIndex, this.totalCount);
      startIndex = range.start;
      count = range.end - range.start;
      console.log(`[DEBUG PhotoSet.loadWindow] Calculated range - startIndex: ${startIndex}, count: ${count}`);
    }

    console.log(`[DEBUG PhotoSet.loadWindow] Loading window [${startIndex}, ${startIndex + count}]`);

    try {
      // Build request for session endpoint
      const request = {
        startIndex,
        count: count || 200
      };

      // Include session ID if available (reuse existing session)
      if (this.sessionId) {
        request.sessionId = this.sessionId;
        console.log(`[DEBUG PhotoSet.loadWindow] Reusing session: ${this.sessionId}`);
      } else {
        // First request - include definition to create session
        request.definition = {
          context: this.definition.type,
          searchQuery: this.definition.searchQuery,
          searchAlpha: this.definition.searchAlpha,
          collectionId: this.definition.id,
          sortBy: this.definition.sortBy || 'capturedAt',
          sortOrder: this.definition.sortOrder || 'desc'
        };
        console.log(`[DEBUG PhotoSet.loadWindow] Creating new session with definition:`, request.definition);
      }

      console.log(`[DEBUG PhotoSet.loadWindow] Calling API: /api/photosets/query with request:`, request);

      // Use session-aware endpoint
      const response = await this.api.post('/api/photosets/query', request);
      console.log(`[DEBUG PhotoSet.loadWindow] API Response:`, response);

      // Store session ID for subsequent requests
      if (response.sessionId) {
        this.sessionId = response.sessionId;
        console.log(`[DEBUG PhotoSet.loadWindow] Session ID: ${this.sessionId}`);
      }

      // Store photos in window cache
      this.window.setRange(response.startIndex, response.photos);
      console.log(`[DEBUG PhotoSet.loadWindow] Stored ${response.photos.length} photos in window cache`);

      return response;
    } catch (error) {
      console.error('[DEBUG PhotoSet.loadWindow] ERROR:', error);
      throw error;
    }
  }

  /**
   * Get photo's index in the current set context
   */
  async getPhotoIndex(photoId) {
    const response = await this.api.get(`/api/photos/${photoId}/index`, {
      context: this.definition.type,
      collectionId: this.definition.id,
      searchQuery: this.definition.searchQuery,
      searchAlpha: this.definition.searchAlpha,
      sortBy: this.definition.sortBy || 'capturedAt',
      sortOrder: this.definition.sortOrder || 'desc',
      filters: this.definition.filters ? JSON.stringify(this.definition.filters) : undefined
    });

    return response;
  }

  /**
   * Fetch a single photo at index (fallback for cache misses)
   * Uses session endpoint to ensure consistency
   */
  async fetchPhotoAtIndex(index) {
    // Build request using session if available
    const request = {
      startIndex: index,
      count: 1
    };

    if (this.sessionId) {
      request.sessionId = this.sessionId;
    } else {
      // Shouldn't happen (session created during initialization), but provide fallback
      request.definition = {
        context: this.definition.type,
        searchQuery: this.definition.searchQuery,
        searchAlpha: this.definition.searchAlpha,
        collectionId: this.definition.id,
        sortBy: this.definition.sortBy || 'capturedAt',
        sortOrder: this.definition.sortOrder || 'desc'
      };
    }

    const response = await this.api.post('/api/photosets/query', request);

    // Store session ID if not already set
    if (response.sessionId && !this.sessionId) {
      this.sessionId = response.sessionId;
    }

    return response.photos[0];
  }

  /**
   * Get current photo metadata
   */
  getCurrentPhoto() {
    return this.currentPhoto;
  }

  /**
   * Get all photos currently in window cache
   * Used by Grid for rendering
   */
  getPhotosInWindow() {
    const photos = [];
    const cache = this.window.cache;

    // Get all cached photos in index order
    const indices = Array.from(cache.keys()).sort((a, b) => a - b);

    for (const index of indices) {
      photos.push(cache.get(index));
    }

    return photos;
  }

  /**
   * Get cached thumbnail for a photo (for optimistic UI)
   */
  getCachedThumbnail(index) {
    const photo = this.window.get(index);
    if (!photo) return null;

    const cached = this.preloader.getCached(photo.id);
    return cached ? cached.url : null;
  }

  /**
   * Get preloaded image
   */
  getPreloadedImage(photoId) {
    return this.preloader.getCached(photoId);
  }

  /**
   * Refresh current photo (e.g., after edit)
   */
  async refreshCurrent() {
    if (this.currentPhoto) {
      const fresh = await this.fetchPhotoAtIndex(this.currentIndex);
      this.currentPhoto = fresh;
      this.window.set(this.currentIndex, fresh);

      this.emit('refresh', { photo: fresh });
    }
  }

  /**
   * Invalidate cache for a specific photo
   */
  invalidatePhoto(photoId) {
    // Remove from window cache
    for (const [index, photo] of this.window.cache) {
      if (photo.id === photoId) {
        this.window.cache.delete(index);
        break;
      }
    }

    // Remove from image cache
    this.preloader.imageCache.delete(photoId);
  }

  /**
   * Clear all caches and session
   */
  clear() {
    this.window.clear();
    this.preloader.clear();
    this.sessionId = null;
    this.currentIndex = -1;
    this.currentPhoto = null;
    this.isInitialized = false;
  }

  /**
   * Invalidate photoset cache (called after mutations)
   */
  static invalidateCache(context, photoId = null) {
    if (!window.photoSetCache) return;

    if (photoId) {
      console.log(`%c[PhotoSet Cache] 🗑️ INVALIDATED (photo deleted)`, 'color: #e74c3c; font-weight: bold', {
        photoId,
        reason: 'Photo was deleted'
      });
      // Specific photo changed - invalidate all caches containing it
      window.photoSetCache.invalidatePhoto(photoId);
    } else if (context) {
      console.log(`%c[PhotoSet Cache] 🗑️ INVALIDATED (${context})`, 'color: #e74c3c; font-weight: bold', {
        context,
        reason: 'Data mutation occurred'
      });
      // Context changed - invalidate all caches for that context
      window.photoSetCache.invalidateContext(context);
    } else {
      // Invalidate everything
      window.photoSetCache.invalidateAll();
    }
  }

  /**
   * Check if can navigate forward
   */
  get canGoNext() {
    return this.currentIndex < this.totalCount - 1;
  }

  /**
   * Check if can navigate backward
   */
  get canGoPrevious() {
    return this.currentIndex > 0;
  }

  /**
   * Get navigation progress
   */
  get progress() {
    return {
      current: this.currentIndex + 1,  // 1-based for display
      total: this.totalCount,
      percentage: this.totalCount > 0 ? (this.currentIndex / this.totalCount) * 100 : 0
    };
  }

  /**
   * Get combined statistics
   */
  getStats() {
    const windowStats = this.window.stats;
    const preloadStats = this.preloader.stats;

    const avgNavigationTime = this.metrics.navigationCount > 0
      ? this.metrics.totalNavigationTime / this.metrics.navigationCount
      : 0;

    const totalAttempts = this.metrics.cacheHits + this.metrics.cacheMisses;
    const cacheHitRate = totalAttempts > 0
      ? this.metrics.cacheHits / totalAttempts
      : 0;

    return {
      navigation: {
        count: this.metrics.navigationCount,
        avgTime: avgNavigationTime,
        lastTime: this.metrics.navigationTime
      },
      cache: {
        hits: this.metrics.cacheHits,
        misses: this.metrics.cacheMisses,
        hitRate: cacheHitRate,
        windowSize: windowStats.size,
        windowRange: windowStats.range
      },
      preload: {
        cachedImages: preloadStats.cachedImages,
        efficiency: preloadStats.efficiency,
        avgLoadTime: preloadStats.avgLoadTime,
        velocity: preloadStats.velocity
      },
      memory: {
        windowSize: windowStats.size,
        imageCache: preloadStats.cachedImages,
        estimatedMB: (windowStats.size * 1 + preloadStats.cachedImages * 3)  // Rough estimate
      }
    };
  }

  /**
   * Event system
   */
  on(event, handler) {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, []);
    }
    this.listeners.get(event).push(handler);
  }

  off(event, handler) {
    if (this.listeners.has(event)) {
      const handlers = this.listeners.get(event);
      const index = handlers.indexOf(handler);
      if (index > -1) {
        handlers.splice(index, 1);
      }
    }
  }

  emit(event, data) {
    if (this.listeners.has(event)) {
      this.listeners.get(event).forEach(handler => {
        try {
          handler(data);
        } catch (error) {
          console.error(`[PhotoSet] Event handler error for '${event}':`, error);
        }
      });
    }
  }
}
