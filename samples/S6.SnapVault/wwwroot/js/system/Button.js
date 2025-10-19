/**
 * Button Component
 * Unified button system with consistent variants, sizes, and accessibility
 * Replaces manual button HTML generation across codebase
 */

import { Icon } from './Icon.js';

export class Button {
  /**
   * Create a button element
   * @param {object} config - Button configuration
   * @param {string} config.label - Button text label (optional if icon-only)
   * @param {string} config.icon - Icon name from registry (optional)
   * @param {string} config.variant - Button variant: 'default' | 'primary' | 'destructive' | 'ghost'
   * @param {string} config.size - Button size: 'sm' | 'md' | 'lg'
   * @param {function} config.onClick - Click handler
   * @param {boolean} config.disabled - Disabled state
   * @param {string} config.ariaLabel - ARIA label (defaults to label)
   * @param {string} config.className - Additional CSS classes
   * @param {string} config.dataAction - data-action attribute value
   * @param {object} config.dataset - Additional data-* attributes
   * @returns {HTMLButtonElement} Button element
   */
  static create(config) {
    const {
      label = null,
      icon = null,
      variant = 'default',
      size = 'md',
      onClick = null,
      disabled = false,
      ariaLabel = label,
      className = '',
      dataAction = null,
      dataset = {}
    } = config;

    const btn = document.createElement('button');

    // Base classes
    btn.className = `btn-action ${className}`.trim();

    // Add variant class
    if (variant === 'destructive') {
      btn.classList.add('btn-destructive');
    } else if (variant === 'primary') {
      btn.classList.add('btn-primary');
    } else if (variant === 'ghost') {
      btn.classList.add('btn-ghost');
    }

    // Add size class
    if (size !== 'md') {
      btn.classList.add(`btn-${size}`);
    }

    // Accessibility
    if (ariaLabel) {
      btn.setAttribute('aria-label', ariaLabel);
    }

    // Disabled state
    btn.disabled = disabled;

    // data-action attribute (for existing event delegation patterns)
    if (dataAction) {
      btn.dataset.action = dataAction;
    }

    // Additional dataset attributes
    Object.entries(dataset).forEach(([key, value]) => {
      btn.dataset[key] = value;
    });

    // Build button content
    if (icon) {
      btn.appendChild(Icon.create(icon, { size: this.getSizePixels(size) }));
    }

    if (label) {
      const labelSpan = document.createElement('span');
      labelSpan.className = 'action-label';
      labelSpan.textContent = label;
      btn.appendChild(labelSpan);
    }

    // Attach click handler
    if (onClick) {
      btn.addEventListener('click', onClick);
    }

    return btn;
  }

  /**
   * Create multiple buttons as a group
   * @param {array} buttons - Array of button configs
   * @param {object} options - Group options
   * @param {string} options.className - Container class name
   * @returns {HTMLDivElement} Button group container
   */
  static createGroup(buttons, options = {}) {
    const { className = 'actions-grid' } = options;

    const group = document.createElement('div');
    group.className = className;

    buttons.forEach(btnConfig => {
      group.appendChild(Button.create(btnConfig));
    });

    return group;
  }

  /**
   * Get pixel size for button size variant
   * @param {string} size - Size variant
   * @returns {number} Pixel size for icons
   */
  static getSizePixels(size) {
    const sizeMap = {
      sm: 16,
      md: 20,
      lg: 24
    };
    return sizeMap[size] || 20;
  }

  /**
   * Update button loading state
   * @param {HTMLButtonElement} btn - Button element
   * @param {boolean} loading - Loading state
   * @param {string} loadingText - Text to show when loading
   */
  static setLoading(btn, loading, loadingText = 'Loading...') {
    if (!btn) return;

    if (loading) {
      btn.disabled = true;
      btn.classList.add('loading');
      btn.dataset.originalText = btn.querySelector('.action-label')?.textContent || '';

      const label = btn.querySelector('.action-label');
      if (label) {
        label.textContent = loadingText;
      }

      // Replace icon with spinner if present
      const icon = btn.querySelector('.icon');
      if (icon) {
        icon.dataset.originalIcon = icon.outerHTML;
        icon.outerHTML = Icon.render('refresh', {
          className: 'icon-spin',
          size: this.getSizePixels('md')
        });
      }
    } else {
      btn.disabled = false;
      btn.classList.remove('loading');

      const label = btn.querySelector('.action-label');
      if (label && btn.dataset.originalText) {
        label.textContent = btn.dataset.originalText;
        delete btn.dataset.originalText;
      }

      // Restore original icon
      const spinner = btn.querySelector('.icon-spin');
      if (spinner && btn.dataset.originalIcon) {
        spinner.outerHTML = btn.dataset.originalIcon;
        delete btn.dataset.originalIcon;
      }
    }
  }
}

/**
 * IconButton Component
 * Specialized button variant for icon-only buttons
 */
export class IconButton extends Button {
  /**
   * Create an icon-only button
   * @param {object} config - Button configuration (same as Button.create)
   * @returns {HTMLButtonElement} Button element
   */
  static create(config) {
    const {
      icon,
      size = 'md',
      variant = 'ghost',
      ariaLabel,
      className = '',
      href = null,
      ...restConfig
    } = config;

    if (!icon) {
      console.warn('[IconButton] Icon is required for IconButton');
      return super.create(config);
    }

    // If href is provided, create an anchor instead
    if (href) {
      const link = document.createElement('a');
      link.href = href;
      link.className = `btn-icon ${className}`.trim();
      link.setAttribute('aria-label', ariaLabel || icon);

      link.appendChild(Icon.create(icon, { size: this.getSizePixels(size) }));

      return link;
    }

    // Create button with icon only
    return super.create({
      ...restConfig,
      icon,
      label: null, // Force no label
      size,
      variant,
      ariaLabel: ariaLabel || icon,
      className: `btn-icon ${className}`.trim()
    });
  }
}
