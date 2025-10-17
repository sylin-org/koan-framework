/**
 * View Preset Configuration
 * Defines professional view modes with automatic resolution matching
 */

export const VIEW_PRESETS = {
  gallery: {
    id: 'gallery',
    label: 'Gallery',
    description: 'Large tiles, maximum detail',
    icon: 'grid-2x2',
    columns: {
      mobile: 1,
      tablet: 2,
      desktop: 3,
      wide: 4,
      ultra: 4
    },
    imageTier: 'gallery', // Always use gallery tier
    minWidth: 350, // Minimum px per tile
    order: 1
  },

  comfortable: {
    id: 'comfortable',
    label: 'Comfortable',
    description: 'Medium tiles, balanced view',
    icon: 'grid-3x3',
    columns: {
      mobile: 2,
      tablet: 3,
      desktop: 4,
      wide: 5,
      ultra: 6
    },
    imageTier: 'auto', // Smart selection based on display
    minWidth: 300,
    order: 2
  },

  cozy: {
    id: 'cozy',
    label: 'Cozy',
    description: 'Smaller tiles, see more',
    icon: 'grid-4x4',
    columns: {
      mobile: 3,
      tablet: 4,
      desktop: 6,
      wide: 7,
      ultra: 8
    },
    imageTier: 'auto',
    minWidth: 200,
    order: 3
  },

  compact: {
    id: 'compact',
    label: 'Compact',
    description: 'Tiny tiles, maximum overview',
    icon: 'grid-5x5',
    columns: {
      mobile: 4,
      tablet: 6,
      desktop: 8,
      wide: 10,
      ultra: 12
    },
    imageTier: 'masonry', // Always use masonry tier
    minWidth: 150,
    order: 4
  }
};

/**
 * Get responsive column count based on viewport width
 */
export function getResponsiveColumns(preset, viewportWidth) {
  if (viewportWidth >= 3840) return preset.columns.ultra;    // 4K+
  if (viewportWidth >= 2560) return preset.columns.wide;     // QHD/4K
  if (viewportWidth >= 1920) return preset.columns.desktop;  // 1080p
  if (viewportWidth >= 1024) return preset.columns.tablet;   // Tablet
  return preset.columns.mobile;                               // Mobile
}

/**
 * Smart resolution selection algorithm
 * Determines optimal image tier based on display characteristics
 */
export function selectOptimalImageTier(preset, viewportWidth, devicePixelRatio = window.devicePixelRatio || 1) {
  // Explicit tier override (gallery, compact modes)
  if (preset.imageTier !== 'auto') {
    return preset.imageTier;
  }

  // Calculate effective pixel density needed per tile
  const columns = getResponsiveColumns(preset, viewportWidth);
  const tileWidth = viewportWidth / columns;
  const effectivePixels = tileWidth * devicePixelRatio;

  // Match tier to display requirements
  // Gallery tier (1200px): For large tiles on high-res displays
  if (effectivePixels >= 600) {
    return 'gallery';
  }

  // Retina tier (600px): For medium tiles on retina/4K
  if (effectivePixels >= 300) {
    return 'retina';
  }

  // Masonry tier (300px): For smaller tiles or standard displays
  return 'masonry';
}

/**
 * Migration: Map old numeric density to new preset
 */
export function migrateOldDensity(density) {
  const DENSITY_TO_PRESET = {
    3: 'comfortable',
    4: 'cozy',
    6: 'compact'
  };

  return DENSITY_TO_PRESET[density] || 'comfortable';
}

/**
 * Get viewport breakpoint name
 */
export function getViewportBreakpoint(viewportWidth) {
  if (viewportWidth >= 3840) return 'ultra';
  if (viewportWidth >= 2560) return 'wide';
  if (viewportWidth >= 1920) return 'desktop';
  if (viewportWidth >= 1024) return 'tablet';
  return 'mobile';
}
