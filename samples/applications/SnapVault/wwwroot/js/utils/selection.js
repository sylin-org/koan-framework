/**
 * Selection Utilities
 * Helper functions for photo selection operations
 */

import { pluralize } from './html.js';

/**
 * Get selected photo IDs with optional validation and user feedback
 * @param {Set} selectedPhotos - Set of selected photo IDs
 * @param {object} toast - Toast component (optional, for showing "no selection" message)
 * @returns {string[]|null} - Array of photo IDs, or null if none selected
 *
 * @example
 * const photoIds = getSelectedPhotoIds(app.state.selectedPhotos, app.components.toast);
 * if (!photoIds) return; // User was notified via toast
 */
export function getSelectedPhotoIds(selectedPhotos, toast = null) {
  const photoIds = Array.from(selectedPhotos);

  if (photoIds.length === 0) {
    if (toast) {
      toast.show('No photos selected', { icon: 'ℹ️', duration: 2000 });
    }
    return null;
  }

  return photoIds;
}

/**
 * Format action message with photo count
 * @param {number} count - Number of photos
 * @param {string} action - Action performed (e.g., "deleted", "added", "removed")
 * @param {object} options - Additional options
 * @returns {string} - Formatted message
 *
 * @example
 * formatActionMessage(3, 'deleted') // => 'Deleted 3 photos'
 * formatActionMessage(1, 'added', { target: 'Favorites' }) // => 'Added 1 photo to Favorites'
 */
export function formatActionMessage(count, action, options = {}) {
  const { target = null, from = null } = options;

  let message = `${action.charAt(0).toUpperCase() + action.slice(1)} ${count} ${pluralize(count, 'photo')}`;

  if (target) {
    message += ` to ${target}`;
  }
  if (from) {
    message += ` from ${from}`;
  }

  return message;
}

/**
 * Clear selection across all selection systems
 * Handles both brush selection (photoSelection) and click selection (app.state)
 * @param {object} app - App instance
 */
export function clearAllSelections(app) {
  // Clear app state
  if (app.clearSelection) {
    app.clearSelection();
  }

  // Clear brush selection
  if (app.components.photoSelection) {
    app.components.photoSelection.clearSelection();
  }

  // Clear text selection
  window.getSelection().removeAllRanges();
}
