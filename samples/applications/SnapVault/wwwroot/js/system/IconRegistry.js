/**
 * Icon Registry
 * Centralized icon definitions to eliminate SVG duplication across components
 * Single source of truth for all application icons
 */

export const IconRegistry = {
  // Most common icons first

  trash: {
    viewBox: '0 0 24 24',
    paths: [
      'M3 6h18',
      'M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'
    ],
    type: 'destructive'
  },

  star: {
    viewBox: '0 0 24 24',
    paths: ['M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2'],
    type: 'action',
    fillable: true
  },

  heart: {
    viewBox: '0 0 24 24',
    paths: ['M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z'],
    type: 'action',
    fillable: true
  },

  download: {
    viewBox: '0 0 24 24',
    paths: [
      'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4',
      'M7 10l5 5 5-5',
      'M12 15V3'
    ],
    type: 'action'
  },

  upload: {
    viewBox: '0 0 24 24',
    paths: [
      'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4',
      'M17 8l-5-5-5 5',
      'M12 3v12'
    ],
    type: 'action'
  },

  edit: {
    viewBox: '0 0 24 24',
    paths: [
      'M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7',
      'M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z'
    ],
    type: 'action'
  },

  copy: {
    viewBox: '0 0 24 24',
    paths: [
      'M9 9h13v13H9z',
      'M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1'
    ],
    type: 'action'
  },

  x: {
    viewBox: '0 0 24 24',
    paths: [
      'M18 6L6 18',
      'M6 6l12 12'
    ],
    type: 'action'
  },

  plus: {
    viewBox: '0 0 24 24',
    paths: [
      'M12 5v14',
      'M5 12h14'
    ],
    type: 'action'
  },

  folder: {
    viewBox: '0 0 24 24',
    paths: ['M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z'],
    type: 'navigation'
  },

  folderPlus: {
    viewBox: '0 0 24 24',
    paths: [
      'M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z',
      'M12 11v6',
      'M9 14h6'
    ],
    type: 'action'
  },

  refresh: {
    viewBox: '0 0 24 24',
    paths: [
      'M23 4v6h-6',
      'M1 20v-6h6',
      'M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15'
    ],
    type: 'action'
  },

  sparkles: {
    viewBox: '0 0 24 24',
    paths: [
      'M12 2l2 7 7 2-7 2-2 7-2-7-7-2 7-2z',
      'M19 9l1 3 3 1-3 1-1 3-1-3-3-1 3-1z'
    ],
    type: 'action'
  },

  settings: {
    viewBox: '0 0 24 24',
    paths: [
      'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
      'M12 1v6m0 6v6',
      'M4.2 4.2l4.2 4.2m7.4 0l4.2-4.2',
      'M1 12h6m6 0h6',
      'M4.2 19.8l4.2-4.2m7.4 0l4.2 4.2'
    ],
    type: 'navigation'
  },

  grid: {
    viewBox: '0 0 24 24',
    paths: [
      'M3 3h7v7H3z',
      'M14 3h7v7h-7z',
      'M14 14h7v7h-7z',
      'M3 14h7v7H3z'
    ],
    type: 'navigation'
  },

  calendar: {
    viewBox: '0 0 24 24',
    paths: [
      'M3 4h18v18H3z',
      'M16 2v4',
      'M8 2v4',
      'M3 10h18'
    ],
    type: 'navigation'
  },

  image: {
    viewBox: '0 0 24 24',
    paths: [
      'M3 3h18v18H3z',
      'M8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z',
      'M21 15l-5-5L5 21'
    ],
    type: 'content'
  },

  chevronDown: {
    viewBox: '0 0 24 24',
    paths: ['M6 9l6 6 6-6'],
    type: 'navigation'
  },

  chevronUp: {
    viewBox: '0 0 24 24',
    paths: ['M18 15l-6-6-6 6'],
    type: 'navigation'
  },

  chevronLeft: {
    viewBox: '0 0 24 24',
    paths: ['M15 18l-6-6 6-6'],
    type: 'navigation'
  },

  chevronRight: {
    viewBox: '0 0 24 24',
    paths: ['M9 18l6-6-6-6'],
    type: 'navigation'
  },

  check: {
    viewBox: '0 0 24 24',
    paths: ['M20 6L9 17l-5-5'],
    type: 'status'
  },

  checkbox: {
    viewBox: '0 0 24 24',
    paths: ['M3 3h18v18H3z'],
    type: 'action'
  },

  checkboxChecked: {
    viewBox: '0 0 24 24',
    paths: [
      'M3 3h18v18H3z',
      'M9 12l2 2 4-4'
    ],
    type: 'action',
    fillable: true
  },

  info: {
    viewBox: '0 0 24 24',
    paths: [
      'M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10z',
      'M12 16v-4',
      'M12 8h.01'
    ],
    type: 'status'
  },

  warning: {
    viewBox: '0 0 24 24',
    paths: [
      'M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z',
      'M12 9v4',
      'M12 17h.01'
    ],
    type: 'status'
  },

  search: {
    viewBox: '0 0 24 24',
    paths: [
      'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z',
      'M21 21l-4.35-4.35'
    ],
    type: 'action'
  }
};

/**
 * Get icon definition by name
 * @param {string} name - Icon name from registry
 * @returns {object|null} Icon definition or null if not found
 */
export function getIcon(name) {
  const icon = IconRegistry[name];
  if (!icon) {
    console.warn(`[IconRegistry] Icon "${name}" not found in registry`);
    return null;
  }
  return icon;
}

/**
 * Get all icons of a specific type
 * @param {string} type - Icon type (action, navigation, destructive, etc.)
 * @returns {object} Object of icon definitions filtered by type
 */
export function getIconsByType(type) {
  return Object.entries(IconRegistry)
    .filter(([_, icon]) => icon.type === type)
    .reduce((acc, [name, icon]) => ({ ...acc, [name]: icon }), {});
}

/**
 * Check if icon exists in registry
 * @param {string} name - Icon name to check
 * @returns {boolean} True if icon exists
 */
export function hasIcon(name) {
  return name in IconRegistry;
}
