/**
 * StateManager - Centralized State Management
 * Single source of truth for application state with reactive updates
 */

export class StateManager {
  constructor() {
    this.state = {
      // Photo Management
      photos: [],
      selectedPhotos: new Set(),
      currentPage: 1,
      hasMorePages: false,
      totalPhotosCount: 0,
      loadingMore: false,
      activeFilter: '',

      // Collections
      collections: [],

      // View State - Single source of truth
      activeView: {
        type: 'all-photos', // 'all-photos' | 'favorites' | 'collection'
        id: null            // null for all-photos/favorites, collectionId for collections
      },

      // UI State
      viewPreset: 'comfortable',

      // Events
      events: []
    };

    // Listeners for reactive updates
    this.listeners = new Map();
  }

  /**
   * Subscribe to state changes
   * @param {string} key - State key to watch
   * @param {Function} callback - Callback function(newValue, oldValue)
   * @returns {Function} Unsubscribe function
   */
  subscribe(key, callback) {
    if (!this.listeners.has(key)) {
      this.listeners.set(key, []);
    }
    this.listeners.get(key).push(callback);

    // Return unsubscribe function
    return () => {
      const callbacks = this.listeners.get(key);
      if (callbacks) {
        this.listeners.set(key, callbacks.filter(cb => cb !== callback));
      }
    };
  }

  /**
   * Get state value
   * @param {string} key - State key
   * @returns {any} State value
   */
  get(key) {
    return this.state[key];
  }

  /**
   * Set state value and notify listeners
   * @param {string} key - State key
   * @param {any} value - New value
   */
  set(key, value) {
    const oldValue = this.state[key];
    this.state[key] = value;

    // Notify listeners
    const callbacks = this.listeners.get(key);
    if (callbacks) {
      callbacks.forEach(cb => cb(value, oldValue));
    }

    // Special handling for activeView changes
    if (key === 'activeView') {
      console.log('[StateManager] Active view changed:', value);
    }
  }

  /**
   * Update multiple state values atomically
   * @param {object} updates - Object with key-value pairs to update
   */
  setMultiple(updates) {
    Object.entries(updates).forEach(([key, value]) => {
      this.set(key, value);
    });
  }

  /**
   * Clear selection
   */
  clearSelection() {
    this.set('selectedPhotos', new Set());
  }

  /**
   * Add photo to selection
   * @param {string} photoId - Photo ID
   */
  selectPhoto(photoId) {
    const selected = new Set(this.state.selectedPhotos);
    selected.add(photoId);
    this.set('selectedPhotos', selected);
  }

  /**
   * Remove photo from selection
   * @param {string} photoId - Photo ID
   */
  deselectPhoto(photoId) {
    const selected = new Set(this.state.selectedPhotos);
    selected.delete(photoId);
    this.set('selectedPhotos', selected);
  }

  /**
   * Toggle photo selection
   * @param {string} photoId - Photo ID
   */
  togglePhotoSelection(photoId) {
    if (this.state.selectedPhotos.has(photoId)) {
      this.deselectPhoto(photoId);
    } else {
      this.selectPhoto(photoId);
    }
  }

  /**
   * Set active view (single source of truth)
   * @param {string} type - View type ('all-photos', 'favorites', 'collection')
   * @param {string|null} id - Collection ID (for collection views)
   */
  setActiveView(type, id = null) {
    this.set('activeView', { type, id });
  }

  /**
   * Check if currently viewing a collection
   * @returns {boolean}
   */
  isViewingCollection() {
    return this.state.activeView.type === 'collection';
  }

  /**
   * Get current collection (if viewing one)
   * @returns {object|null} Collection object or null
   */
  getCurrentCollection() {
    if (!this.isViewingCollection()) return null;
    return this.state.collections.find(c => c.id === this.state.activeView.id);
  }

  /**
   * Reset to initial state
   */
  reset() {
    this.set('photos', []);
    this.clearSelection();
    this.set('currentPage', 1);
    this.set('hasMorePages', false);
    this.set('totalPhotosCount', 0);
  }
}
