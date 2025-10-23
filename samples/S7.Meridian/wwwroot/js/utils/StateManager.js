/**
 * StateManager - Reactive state management with subscriptions
 * Borrowed from SnapVault for centralized application state
 */
export class StateManager {
  constructor(initialState = {}) {
    this.state = initialState;
    this.subscriptions = new Map();
  }

  /**
   * Get a value from state
   * @param {string} key - State key
   * @returns {*} State value
   */
  get(key) {
    return this.state[key];
  }

  /**
   * Set a value in state and notify subscribers
   * @param {string} key - State key
   * @param {*} value - State value
   */
  set(key, value) {
    const oldValue = this.state[key];

    // Only update and notify if value actually changed
    if (oldValue !== value) {
      this.state[key] = value;
      this.notify(key, value, oldValue);
    }
  }

  /**
   * Update multiple state values at once
   * @param {Object} updates - Object with key-value pairs to update
   */
  update(updates) {
    Object.entries(updates).forEach(([key, value]) => {
      this.set(key, value);
    });
  }

  /**
   * Subscribe to state changes for a specific key
   * @param {string} key - State key to watch
   * @param {Function} callback - Callback function (newValue, oldValue)
   * @returns {Function} Unsubscribe function
   */
  subscribe(key, callback) {
    if (!this.subscriptions.has(key)) {
      this.subscriptions.set(key, []);
    }

    this.subscriptions.get(key).push(callback);

    // Return unsubscribe function
    return () => this.unsubscribe(key, callback);
  }

  /**
   * Unsubscribe from state changes
   * @param {string} key - State key
   * @param {Function} callback - Callback to remove
   */
  unsubscribe(key, callback) {
    if (!this.subscriptions.has(key)) return;

    const callbacks = this.subscriptions.get(key);
    const index = callbacks.indexOf(callback);

    if (index !== -1) {
      callbacks.splice(index, 1);
    }

    if (callbacks.length === 0) {
      this.subscriptions.delete(key);
    }
  }

  /**
   * Notify subscribers of a state change
   * @param {string} key - State key that changed
   * @param {*} newValue - New value
   * @param {*} oldValue - Old value
   */
  notify(key, newValue, oldValue) {
    if (!this.subscriptions.has(key)) return;

    const callbacks = this.subscriptions.get(key);
    callbacks.forEach(callback => {
      try {
        callback(newValue, oldValue);
      } catch (error) {
        console.error(`Error in state subscriber for "${key}":`, error);
      }
    });
  }

  /**
   * Reset state to initial values
   * @param {Object} initialState - New initial state
   */
  reset(initialState = {}) {
    const keys = new Set([...Object.keys(this.state), ...Object.keys(initialState)]);

    keys.forEach(key => {
      this.set(key, initialState[key]);
    });
  }

  /**
   * Get a snapshot of the current state
   * @returns {Object} Copy of current state
   */
  getSnapshot() {
    return { ...this.state };
  }
}
