/**
 * EventBus - Decoupled Component Communication
 * Allows components to communicate without direct references
 */

export class EventBus {
  constructor() {
    this.events = new Map();
    this.debugMode = false; // Set to true for event logging
  }

  /**
   * Subscribe to an event
   * @param {string} event - Event name
   * @param {Function} callback - Callback function
   * @returns {Function} Unsubscribe function
   */
  on(event, callback) {
    if (!this.events.has(event)) {
      this.events.set(event, []);
    }
    this.events.get(event).push(callback);

    if (this.debugMode) {
      console.log(`[EventBus] Subscribed to: ${event}`);
    }

    // Return unsubscribe function
    return () => this.off(event, callback);
  }

  /**
   * Subscribe to an event (one-time only)
   * @param {string} event - Event name
   * @param {Function} callback - Callback function
   */
  once(event, callback) {
    const onceCallback = (...args) => {
      callback(...args);
      this.off(event, onceCallback);
    };
    this.on(event, onceCallback);
  }

  /**
   * Unsubscribe from an event
   * @param {string} event - Event name
   * @param {Function} callback - Callback function to remove
   */
  off(event, callback) {
    const callbacks = this.events.get(event);
    if (callbacks) {
      this.events.set(event, callbacks.filter(cb => cb !== callback));

      if (this.debugMode) {
        console.log(`[EventBus] Unsubscribed from: ${event}`);
      }
    }
  }

  /**
   * Emit an event
   * @param {string} event - Event name
   * @param {any} data - Event data
   */
  emit(event, data = null) {
    const callbacks = this.events.get(event);

    if (this.debugMode) {
      console.log(`[EventBus] Emitting: ${event}`, data);
    }

    if (callbacks) {
      callbacks.forEach(cb => {
        try {
          cb(data);
        } catch (error) {
          console.error(`[EventBus] Error in event handler for "${event}":`, error);
        }
      });
    }
  }

  /**
   * Clear all event listeners for a specific event
   * @param {string} event - Event name
   */
  clear(event) {
    this.events.delete(event);

    if (this.debugMode) {
      console.log(`[EventBus] Cleared all listeners for: ${event}`);
    }
  }

  /**
   * Clear all event listeners
   */
  clearAll() {
    this.events.clear();

    if (this.debugMode) {
      console.log('[EventBus] Cleared all listeners');
    }
  }

  /**
   * Get list of all registered events
   * @returns {string[]} Array of event names
   */
  getEvents() {
    return Array.from(this.events.keys());
  }

  /**
   * Get listener count for an event
   * @param {string} event - Event name
   * @returns {number} Number of listeners
   */
  getListenerCount(event) {
    return this.events.get(event)?.length || 0;
  }

  /**
   * Enable/disable debug mode
   * @param {boolean} enabled - Enable debug logging
   */
  setDebugMode(enabled) {
    this.debugMode = enabled;
  }
}

// Standard events used across the application
export const Events = {
  // Photo events
  PHOTOS_LOADED: 'photos:loaded',
  PHOTO_SELECTED: 'photo:selected',
  PHOTO_DESELECTED: 'photo:deselected',
  SELECTION_CLEARED: 'selection:cleared',
  PHOTO_DELETED: 'photo:deleted',
  PHOTO_FAVORITED: 'photo:favorited',

  // Collection events
  COLLECTIONS_LOADED: 'collections:loaded',
  COLLECTION_CREATED: 'collection:created',
  COLLECTION_UPDATED: 'collection:updated',
  COLLECTION_DELETED: 'collection:deleted',
  COLLECTION_VIEW_CHANGED: 'collection:view:changed',

  // View events
  VIEW_CHANGED: 'view:changed',
  VIEW_PRESET_CHANGED: 'view:preset:changed',

  // UI events
  TOAST_SHOW: 'toast:show',
  MODAL_OPENED: 'modal:opened',
  MODAL_CLOSED: 'modal:closed'
};
