/**
 * HTML Utilities
 * Shared utilities for safe HTML rendering and text manipulation
 */

/**
 * Escape HTML special characters to prevent XSS
 * @param {string} text - Text to escape
 * @returns {string} - HTML-safe string
 */
export function escapeHtml(text) {
  if (text == null) return '';
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

/**
 * Create element from HTML string
 * @param {string} html - HTML string
 * @returns {Element} - Created element
 */
export function createElement(html) {
  const template = document.createElement('template');
  template.innerHTML = html.trim();
  return template.content.firstChild;
}

/**
 * Pluralize word based on count
 * @param {number} count - Count
 * @param {string} singular - Singular form
 * @param {string} plural - Plural form (optional, defaults to singular + 's')
 * @returns {string} - Pluralized word
 *
 * @example
 * pluralize(1, 'photo') // => 'photo'
 * pluralize(5, 'photo') // => 'photos'
 * pluralize(1, 'category', 'categories') // => 'category'
 * pluralize(2, 'category', 'categories') // => 'categories'
 */
export function pluralize(count, singular, plural = null) {
  return count === 1 ? singular : (plural || singular + 's');
}

/**
 * Format photo count message with proper pluralization
 * @param {number} count - Number of photos
 * @param {string} prefix - Prefix text (optional)
 * @returns {string} - Formatted message
 *
 * @example
 * formatPhotoCount(1) // => '1 photo'
 * formatPhotoCount(5) // => '5 photos'
 * formatPhotoCount(3, 'Added') // => 'Added 3 photos'
 */
export function formatPhotoCount(count, prefix = '') {
  const text = `${count} ${pluralize(count, 'photo')}`;
  return prefix ? `${prefix} ${text}` : text;
}
