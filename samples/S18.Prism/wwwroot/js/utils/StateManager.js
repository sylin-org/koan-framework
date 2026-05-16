/**
 * StateManager - Centralized State Management
 * Single source of truth for application state with reactive updates
 */

export class StateManager {
    constructor() {
        this.state = {};
        this.subscribers = new Map();
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
     * Set state value and notify subscribers
     * @param {string} key - State key
     * @param {any} value - New value
     */
    set(key, value) {
        const oldValue = this.state[key];
        this.state[key] = value;

        const callbacks = this.subscribers.get(key);
        if (callbacks) {
            callbacks.forEach(cb => {
                try {
                    cb(value, oldValue);
                } catch (error) {
                    console.error(`[StateManager] Error in subscriber for "${key}":`, error);
                }
            });
        }
    }

    /**
     * Subscribe to state changes for a key
     * @param {string} key - State key to watch
     * @param {Function} callback - Callback function(newValue, oldValue)
     * @returns {Function} Unsubscribe function
     */
    subscribe(key, callback) {
        if (!this.subscribers.has(key)) {
            this.subscribers.set(key, []);
        }
        this.subscribers.get(key).push(callback);

        return () => {
            const callbacks = this.subscribers.get(key);
            if (callbacks) {
                this.subscribers.set(key, callbacks.filter(cb => cb !== callback));
            }
        };
    }

    /**
     * Update multiple state values
     * @param {object} updates - Key-value pairs to update
     */
    setMultiple(updates) {
        Object.entries(updates).forEach(([key, value]) => {
            this.set(key, value);
        });
    }
}
