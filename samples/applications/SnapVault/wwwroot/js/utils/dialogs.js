/**
 * Dialog Utilities
 * Standardized confirmation dialogs with consistent messaging
 */

import { pluralize } from './html.js';

/**
 * Show delete confirmation dialog
 * @param {number} count - Number of items
 * @param {string} itemType - Type of item (photo, collection, etc.)
 * @param {object} options - Additional options
 * @returns {boolean} - True if confirmed
 *
 * @example
 * if (confirmDelete(5, 'photo')) {
 *   // Delete 5 photos
 * }
 *
 * if (confirmDelete(1, 'collection', { permanentWarning: false })) {
 *   // Delete collection
 * }
 */
export function confirmDelete(count, itemType = 'photo', options = {}) {
  const {
    permanentWarning = true,
    additionalInfo = null,
    customMessage = null
  } = options;

  const plural = count !== 1;

  if (customMessage) {
    return confirm(customMessage);
  }

  const message = [
    `Permanently delete ${count} ${itemType}${plural ? 's' : ''}?`,
    additionalInfo,
    permanentWarning ? 'This cannot be undone.' : null
  ].filter(Boolean).join('\n\n');

  return confirm(message);
}

/**
 * Show collection delete confirmation with photo count info
 * @param {string} collectionName - Name of collection
 * @param {number} photoCount - Number of photos in collection
 * @returns {boolean} - True if confirmed
 *
 * @example
 * if (confirmDeleteCollection('Vacation 2024', 127)) {
 *   // Delete collection
 * }
 */
export function confirmDeleteCollection(collectionName, photoCount) {
  return confirm(
    `Delete collection "${collectionName}"?\n\n` +
    `${photoCount} ${pluralize(photoCount, 'photo')} will remain in your library.`
  );
}

/**
 * Show removal confirmation for photos in a collection
 * @param {number} count - Number of photos to remove
 * @param {string} collectionName - Name of collection
 * @returns {boolean} - True if confirmed
 *
 * @example
 * if (confirmRemoveFromCollection(3, 'Favorites')) {
 *   // Remove from collection
 * }
 */
export function confirmRemoveFromCollection(count, collectionName) {
  const plural = count !== 1;
  return confirm(
    `Remove ${count} ${plural ? 'photos' : 'photo'} from "${collectionName}"?\n\n` +
    `${plural ? 'They' : 'It'} will remain in your library.`
  );
}
