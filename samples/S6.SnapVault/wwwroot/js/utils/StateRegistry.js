/**
 * Centralized State Registry
 * Single source of truth for global application state with event-driven updates
 *
 * Purpose:
 * - Centralize shared values (counts, configs, flags)
 * - Event-driven updates (components subscribe to state changes)
 * - Avoid manual UI updates scattered across codebase
 * - Single place to update state, automatic UI propagation
 */

export class StateRegistry {
  constructor() {
    this.state = {
      // Photo counts (always accurate, fetched from API)
      counts: {
        totalPhotos: 0,
        favorites: 0,
        collections: {} // collectionId -> photoCount
      },

      // Current view context
      view: {
        type: 'all-photos', // 'all-photos' | 'favorites' | 'collection'
        collectionId: null,
        collectionName: null
      },

      // Loaded photos (from PhotoSet)
      photos: [],
      loadedPhotosCount: 0,

      // Infinite scroll state
      hasMorePages: true,

      // Selection state
      selectedPhotos: new Set()
    };

    // Event listeners: Map<stateKey, Set<callback>>
    this.listeners = new Map();
  }

  /**
   * Subscribe to state changes
   * @param {string} key - State key to watch (e.g., 'counts', 'counts.favorites')
   * @param {function} callback - Called when state changes: callback(newValue, oldValue)
   * @returns {function} Unsubscribe function
   */
  subscribe(key, callback) {
    if (!this.listeners.has(key)) {
      this.listeners.set(key, new Set());
    }
    this.listeners.get(key).add(callback);

    // Return unsubscribe function
    return () => {
      this.listeners.get(key)?.delete(callback);
    };
  }

  /**
   * Update state and notify listeners
   * @param {string} key - State key (supports dot notation: 'counts.favorites')
   * @param {any} value - New value
   */
  update(key, value) {
    const oldValue = this.get(key);

    // Set nested value using dot notation
    this.setNested(this.state, key, value);

    // Notify listeners for this specific key
    this.notify(key, value, oldValue);

    // Notify listeners for parent keys (e.g., 'counts' when 'counts.favorites' changes)
    const parts = key.split('.');
    for (let i = parts.length - 1; i > 0; i--) {
      const parentKey = parts.slice(0, i).join('.');
      const parentValue = this.get(parentKey);
      this.notify(parentKey, parentValue, parentValue); // Parent value hasn't changed, just notify
    }
  }

  /**
   * Get state value (supports dot notation)
   * @param {string} key - State key
   * @returns {any} State value
   */
  get(key) {
    return this.getNested(this.state, key);
  }

  /**
   * Batch update multiple state keys
   * @param {object} updates - Object with key-value pairs to update
   */
  batchUpdate(updates) {
    Object.entries(updates).forEach(([key, value]) => {
      this.update(key, value);
    });
  }

  /**
   * Get nested value using dot notation
   * @private
   */
  getNested(obj, path) {
    return path.split('.').reduce((current, key) => current?.[key], obj);
  }

  /**
   * Set nested value using dot notation
   * @private
   */
  setNested(obj, path, value) {
    const keys = path.split('.');
    const lastKey = keys.pop();
    const target = keys.reduce((current, key) => {
      if (!current[key]) current[key] = {};
      return current[key];
    }, obj);
    target[lastKey] = value;
  }

  /**
   * Notify listeners of state change
   * @private
   */
  notify(key, newValue, oldValue) {
    const listeners = this.listeners.get(key);
    if (listeners) {
      listeners.forEach(callback => {
        try {
          callback(newValue, oldValue);
        } catch (error) {
          console.error(`[StateRegistry] Error in listener for "${key}":`, error);
        }
      });
    }
  }

  /**
   * Debug: Log current state
   */
  dump() {
    console.log('[StateRegistry] Current state:', JSON.parse(JSON.stringify(this.state)));
  }
}
