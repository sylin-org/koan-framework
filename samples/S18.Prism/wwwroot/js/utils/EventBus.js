/**
 * EventBus - Decoupled Component Communication
 * Allows components to communicate without direct references
 */

export class EventBus {
    constructor() {
        this.listeners = new Map();
    }

    /**
     * Subscribe to an event
     * @param {string} event - Event name
     * @param {Function} callback - Callback function
     * @returns {Function} Unsubscribe function
     */
    on(event, callback) {
        if (!this.listeners.has(event)) {
            this.listeners.set(event, []);
        }
        this.listeners.get(event).push(callback);
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
        const callbacks = this.listeners.get(event);
        if (callbacks) {
            this.listeners.set(event, callbacks.filter(cb => cb !== callback));
        }
    }

    /**
     * Emit an event
     * @param {string} event - Event name
     * @param {any} data - Event data
     */
    emit(event, data = null) {
        const callbacks = this.listeners.get(event);
        if (callbacks) {
            callbacks.forEach(cb => {
                try {
                    cb(data);
                } catch (error) {
                    console.error(`[EventBus] Error in handler for "${event}":`, error);
                }
            });
        }
    }

    /**
     * Clear all listeners for a specific event
     * @param {string} event - Event name
     */
    clear(event) {
        this.listeners.delete(event);
    }

    /**
     * Clear all listeners
     */
    clearAll() {
        this.listeners.clear();
    }
}

/** Standard events used across Prism */
export const Events = {
    // Space events
    SPACES_LOADED: 'spaces:loaded',
    SPACE_SELECTED: 'space:selected',
    SPACE_CREATED: 'space:created',

    // Note events
    NOTES_LOADED: 'notes:loaded',
    NOTE_SELECTED: 'note:selected',
    NOTE_DESELECTED: 'note:deselected',
    NOTE_CREATED: 'note:created',
    NOTE_UPDATED: 'note:updated',

    // Search events
    SEARCH_QUERY: 'search:query',
    SEARCH_RESULTS: 'search:results',
    SEARCH_CLEAR: 'search:clear',

    // View events
    VIEW_CHANGED: 'view:changed',
    VIEW_NOTES: 'view:notes',

    // Sidebar filter events
    SOURCE_SELECTED: 'source:selected',
    BRIEF_SELECTED: 'brief:selected',

    // Upload events
    UPLOAD_STARTED: 'upload:started',
    UPLOAD_COMPLETE: 'upload:complete',
    UPLOAD_ERROR: 'upload:error',

    // Finding events
    FINDING_APPROVED: 'finding:approved',
    FINDING_DISMISSED: 'finding:dismissed',

    // Status
    STATUS_UPDATE: 'status:update'
};
