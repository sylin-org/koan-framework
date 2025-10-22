/**
 * PhotoSetCache - Client-side caching for photoset results
 * Provides instant loading by showing cached data while refreshing in background
 */

class PhotoSetCache {
    constructor() {
        this.CACHE_PREFIX = 'photoset_cache:';
        this.CACHE_TTL = 24 * 60 * 60 * 1000; // 1 day
        this.MAX_ENTRIES = 10; // LRU eviction
        this.listeners = new Set();
    }

    /**
     * Generate cache key from photoset definition
     */
    _getCacheKey(definition) {
        const parts = [
            definition.context,
            definition.sortBy || 'capturedAt',
            definition.sortOrder || 'desc'
        ];

        // Include collection ID for collection context
        if (definition.context === 'collection' && definition.collectionId) {
            parts.push(definition.collectionId);
        }

        // Include search query for search context
        if (definition.context === 'search' && definition.searchQuery) {
            parts.push(definition.searchQuery);
            parts.push(String(definition.searchAlpha || 0.5));
        }

        return this.CACHE_PREFIX + parts.join(':');
    }

    /**
     * Get cached photoset if valid
     */
    get(definition) {
        const key = this._getCacheKey(definition);

        try {
            const cached = localStorage.getItem(key);
            if (!cached) return null;

            const data = JSON.parse(cached);

            // Validate structure
            if (!data.session || !data.photos || !data.timestamp || !data.version) {
                console.warn('[PhotoSetCache] Invalid cache structure, clearing:', key);
                this.invalidate(definition);
                return null;
            }

            // Check TTL
            const age = Date.now() - data.timestamp;
            if (age > this.CACHE_TTL) {
                console.log('[PhotoSetCache] Cache expired:', key, 'age:', Math.round(age / 1000 / 60), 'minutes');
                this.invalidate(definition);
                return null;
            }

            // Update LRU timestamp
            data.lastAccessed = Date.now();
            localStorage.setItem(key, JSON.stringify(data));

            console.log(`[PhotoSetCache] ✓ Cache hit for key: ${key.replace(this.CACHE_PREFIX, '')} (age: ${Math.round(age / 1000 / 60)}m)`);
            return data;

        } catch (error) {
            console.error('[PhotoSetCache] Error reading cache:', error);
            this.invalidate(definition);
            return null;
        }
    }

    /**
     * Store photoset in cache
     */
    set(definition, session, photos) {
        const key = this._getCacheKey(definition);

        try {
            const data = {
                version: 1,
                timestamp: Date.now(),
                lastAccessed: Date.now(),
                session: session,
                photos: photos,
                definition: definition
            };

            localStorage.setItem(key, JSON.stringify(data));
            console.log(`[PhotoSetCache] 💾 Stored ${photos.length} photos in key: ${key.replace(this.CACHE_PREFIX, '')}`);

            // LRU cleanup
            this._cleanupOldEntries();

        } catch (error) {
            if (error.name === 'QuotaExceededError') {
                console.warn('[PhotoSetCache] Storage quota exceeded, clearing old entries');
                this._cleanupOldEntries(true); // Aggressive cleanup

                // Retry once
                try {
                    localStorage.setItem(key, JSON.stringify(data));
                } catch (retryError) {
                    console.error('[PhotoSetCache] Failed to cache after cleanup:', retryError);
                }
            } else {
                console.error('[PhotoSetCache] Error writing cache:', error);
            }
        }
    }

    /**
     * Invalidate specific cache entry
     */
    invalidate(definition) {
        const key = this._getCacheKey(definition);
        localStorage.removeItem(key);
        console.log(`[PhotoSetCache] 🗑️ Invalidated key: ${key.replace(this.CACHE_PREFIX, '')}`);
        this._notifyInvalidation(definition.context);
    }

    /**
     * Invalidate all caches for a specific context
     */
    invalidateContext(context) {
        const keys = this._getAllCacheKeys();
        let count = 0;

        for (const key of keys) {
            if (key.includes(`:${context}:`)) {
                localStorage.removeItem(key);
                count++;
            }
        }

        console.log(`[PhotoSetCache] 🗑️ Invalidated ${count} cache ${count === 1 ? 'entry' : 'entries'} for context: ${context}`);
        this._notifyInvalidation(context);
    }

    /**
     * Invalidate all caches containing a specific photo ID
     */
    invalidatePhoto(photoId) {
        const keys = this._getAllCacheKeys();
        let count = 0;

        for (const key of keys) {
            try {
                const cached = localStorage.getItem(key);
                if (!cached) continue;

                const data = JSON.parse(cached);
                if (data.photos && data.photos.some(p => p.id === photoId)) {
                    localStorage.removeItem(key);
                    count++;
                }
            } catch (error) {
                console.error('[PhotoSetCache] Error checking cache entry:', error);
            }
        }

        console.log(`[PhotoSetCache] 🗑️ Invalidated ${count} cache ${count === 1 ? 'entry' : 'entries'} containing photo: ${photoId}`);
        this._notifyInvalidation('all');
    }

    /**
     * Invalidate all caches
     */
    invalidateAll() {
        const keys = this._getAllCacheKeys();
        for (const key of keys) {
            localStorage.removeItem(key);
        }
        console.log('[PhotoSetCache] Cleared all cache entries:', keys.length);
        this._notifyInvalidation('all');
    }

    /**
     * Get all cache keys
     */
    _getAllCacheKeys() {
        const keys = [];
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && key.startsWith(this.CACHE_PREFIX)) {
                keys.push(key);
            }
        }
        return keys;
    }

    /**
     * Clean up old cache entries (LRU eviction)
     */
    _cleanupOldEntries(aggressive = false) {
        const keys = this._getAllCacheKeys();

        if (keys.length <= this.MAX_ENTRIES && !aggressive) {
            return; // No cleanup needed
        }

        // Get all entries with timestamps
        const entries = [];
        for (const key of keys) {
            try {
                const cached = localStorage.getItem(key);
                if (!cached) continue;

                const data = JSON.parse(cached);
                entries.push({
                    key: key,
                    lastAccessed: data.lastAccessed || data.timestamp || 0
                });
            } catch (error) {
                // Invalid entry, remove it
                localStorage.removeItem(key);
            }
        }

        // Sort by last accessed (oldest first)
        entries.sort((a, b) => a.lastAccessed - b.lastAccessed);

        // Remove oldest entries
        const targetCount = aggressive ? Math.floor(this.MAX_ENTRIES / 2) : this.MAX_ENTRIES;
        const toRemove = entries.length - targetCount;

        for (let i = 0; i < toRemove; i++) {
            localStorage.removeItem(entries[i].key);
            console.log('[PhotoSetCache] Evicted old entry:', entries[i].key);
        }
    }

    /**
     * Get cache statistics
     */
    getStats() {
        const keys = this._getAllCacheKeys();
        let totalSize = 0;
        let oldestTimestamp = Date.now();
        let newestTimestamp = 0;

        for (const key of keys) {
            try {
                const cached = localStorage.getItem(key);
                if (!cached) continue;

                totalSize += cached.length;
                const data = JSON.parse(cached);
                if (data.timestamp) {
                    oldestTimestamp = Math.min(oldestTimestamp, data.timestamp);
                    newestTimestamp = Math.max(newestTimestamp, data.timestamp);
                }
            } catch (error) {
                // Ignore invalid entries
            }
        }

        return {
            entryCount: keys.length,
            totalSizeKB: Math.round(totalSize / 1024),
            oldestAgeMinutes: Math.round((Date.now() - oldestTimestamp) / 1000 / 60),
            newestAgeMinutes: Math.round((Date.now() - newestTimestamp) / 1000 / 60)
        };
    }

    /**
     * Register listener for cache invalidation events
     */
    onInvalidate(callback) {
        this.listeners.add(callback);
        return () => this.listeners.delete(callback);
    }

    /**
     * Notify listeners of cache invalidation
     */
    _notifyInvalidation(context) {
        for (const listener of this.listeners) {
            try {
                listener(context);
            } catch (error) {
                console.error('[PhotoSetCache] Error in invalidation listener:', error);
            }
        }
    }
}

// Export singleton instance
window.photoSetCache = new PhotoSetCache();
