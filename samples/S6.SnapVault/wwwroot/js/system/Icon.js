/**
 * Icon Component
 * Renders SVG icons from the centralized IconRegistry
 * Eliminates inline SVG duplication across codebase
 */

import { getIcon } from './IconRegistry.js';

export class Icon {
  /**
   * Render icon as HTML string (for use in template literals)
   * @param {string} name - Icon name from registry
   * @param {object} options - Rendering options
   * @param {number} options.size - Icon size in pixels (default: 20)
   * @param {string} options.fill - Fill color (default: 'none', use 'currentColor' for filled icons)
   * @param {string} options.stroke - Stroke color (default: 'currentColor')
   * @param {number} options.strokeWidth - Stroke width (default: 2)
   * @param {string} options.className - Additional CSS classes
   * @returns {string} SVG markup as string
   */
  static render(name, options = {}) {
    const icon = getIcon(name);
    if (!icon) {
      return `<!-- Icon "${name}" not found -->`;
    }

    const {
      size = 20,
      fill = 'none',
      stroke = 'currentColor',
      strokeWidth = 2,
      className = ''
    } = options;

    // Build SVG paths
    const pathsMarkup = icon.paths.map(path => {
      // Check if path is a simple line/polyline command or full path
      if (path.startsWith('M') || path.includes('L') || path.includes('C')) {
        // Full path definition
        return `<path d="${path}"></path>`;
      } else {
        // Simple line/rect - assume it's a direct element
        // Handle special cases
        if (path.includes('h') && path.includes('v')) {
          // It's a rect
          const coords = path.match(/M?(\d+)\s+(\d+)h(\d+)v(\d+)/);
          if (coords) {
            return `<rect x="${coords[1]}" y="${coords[2]}" width="${coords[3]}" height="${coords[4]}" rx="2" ry="2"></rect>`;
          }
        }
        // Default: treat as path
        return `<path d="${path}"></path>`;
      }
    }).join('');

    return `<svg class="icon ${className}" width="${size}" height="${size}" viewBox="${icon.viewBox}" fill="${fill}" stroke="${stroke}" stroke-width="${strokeWidth}">${pathsMarkup}</svg>`;
  }

  /**
   * Create icon as DOM element
   * @param {string} name - Icon name from registry
   * @param {object} options - Same options as render()
   * @returns {SVGElement} SVG element
   */
  static create(name, options = {}) {
    const markup = Icon.render(name, options);
    const wrapper = document.createElement('div');
    wrapper.innerHTML = markup;
    return wrapper.firstElementChild;
  }

  /**
   * Create filled icon variant (for icons like star, heart)
   * @param {string} name - Icon name from registry
   * @param {boolean} filled - Whether icon should be filled
   * @param {object} options - Additional options
   * @returns {string} SVG markup
   */
  static renderFillable(name, filled = false, options = {}) {
    const icon = getIcon(name);
    if (!icon || !icon.fillable) {
      console.warn(`[Icon] Icon "${name}" is not fillable`);
      return Icon.render(name, options);
    }

    return Icon.render(name, {
      ...options,
      fill: filled ? 'currentColor' : 'none'
    });
  }

  /**
   * Create fillable icon as DOM element
   * @param {string} name - Icon name from registry
   * @param {boolean} filled - Whether icon should be filled
   * @param {object} options - Additional options
   * @returns {SVGElement} SVG element
   */
  static createFillable(name, filled = false, options = {}) {
    const markup = Icon.renderFillable(name, filled, options);
    const wrapper = document.createElement('div');
    wrapper.innerHTML = markup;
    return wrapper.firstElementChild;
  }

  /**
   * Update fill state of existing icon element
   * Useful for toggle interactions (favorite, checkbox, etc.)
   * @param {SVGElement} iconElement - Existing SVG icon element
   * @param {boolean} filled - New fill state
   */
  static updateFill(iconElement, filled) {
    if (!iconElement || iconElement.tagName !== 'svg') {
      console.warn('[Icon] updateFill requires SVG element');
      return;
    }
    iconElement.setAttribute('fill', filled ? 'currentColor' : 'none');
  }

  /**
   * Helper: Render icon for button usage
   * Pre-configured sizes for button contexts
   * @param {string} name - Icon name
   * @param {string} buttonSize - Button size: 'sm' | 'md' | 'lg'
   * @param {object} options - Additional options
   * @returns {string} SVG markup
   */
  static forButton(name, buttonSize = 'md', options = {}) {
    const sizeMap = {
      sm: 16,
      md: 20,
      lg: 24
    };

    return Icon.render(name, {
      size: sizeMap[buttonSize] || 20,
      ...options
    });
  }
}
