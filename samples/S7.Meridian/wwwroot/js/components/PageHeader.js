/**
 * PageHeader - Unified page header component
 *
 * Design Pattern: Consistent header across all views
 * Features:
 * - Breadcrumb navigation
 * - Page title with optional subtitle
 * - Action buttons (primary + secondary)
 * - Responsive layout
 */
import { Breadcrumb } from './Breadcrumb.js';

export class PageHeader {
  constructor(router, eventBus) {
    this.router = router;
    this.eventBus = eventBus;
    this.breadcrumb = new Breadcrumb(router, eventBus);
  }

  /**
   * Render page header
   * @param {Object} options - Configuration object
   * @returns {string} HTML string
   */
  render(options = {}) {
    const {
      title = '',
      subtitle = '',
      breadcrumbs = null, // Array of {label, path, icon?}
      actions = [], // Array of {label, action, variant, icon?}
      showBreadcrumbs = true,
      compact = false
    } = options;

    return `
      <header class="page-header ${compact ? 'page-header-compact' : ''}">
        ${showBreadcrumbs && breadcrumbs ? `
          <div class="page-header-breadcrumb">
            ${this.breadcrumb.render(breadcrumbs)}
          </div>
        ` : ''}

        <div class="page-header-content">
          <div class="page-header-text">
            ${title ? `
              <h1 class="page-header-title">${this.escapeHtml(title)}</h1>
            ` : ''}
            ${subtitle ? `
              <p class="page-header-subtitle">${this.escapeHtml(subtitle)}</p>
            ` : ''}
          </div>

          ${actions.length > 0 ? `
            <div class="page-header-actions">
              ${actions.map(action => this.renderAction(action)).join('')}
            </div>
          ` : ''}
        </div>
      </header>
    `;
  }

  /**
   * Render action button
   */
  renderAction(action) {
    const {
      label,
      action: actionName,
      variant = 'primary', // primary, secondary, danger
      icon = null,
      disabled = false,
      dropdown = null // Array of dropdown items
    } = action;

    const hasDropdown = dropdown && dropdown.length > 0;

    return `
      <div class="page-header-action-wrapper ${hasDropdown ? 'has-dropdown' : ''}">
        <button class="btn btn-${variant} btn-press ${disabled ? 'disabled' : ''}"
                data-action="${this.escapeHtml(actionName)}"
                ${disabled ? 'disabled' : ''}
                ${hasDropdown ? 'data-dropdown-toggle' : ''}>
          ${icon ? `
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              ${icon}
            </svg>
          ` : ''}
          <span>${this.escapeHtml(label)}</span>
          ${hasDropdown ? `
            <svg class="dropdown-chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="6 9 12 15 18 9"></polyline>
            </svg>
          ` : ''}
        </button>

        ${hasDropdown ? `
          <div class="page-header-dropdown" data-dropdown-menu>
            ${dropdown.map(item => this.renderDropdownItem(item)).join('')}
          </div>
        ` : ''}
      </div>
    `;
  }

  /**
   * Render dropdown menu item
   */
  renderDropdownItem(item) {
    const {
      label,
      action,
      icon = null,
      variant = 'default', // default, danger
      separator = false,
      disabled = false
    } = item;

    if (separator) {
      return '<hr class="dropdown-separator" />';
    }

    return `
      <button class="dropdown-item ${variant === 'danger' ? 'dropdown-item-danger' : ''} ${disabled ? 'disabled' : ''}"
              data-action="${this.escapeHtml(action)}"
              ${disabled ? 'disabled' : ''}>
        ${icon ? `
          <svg class="dropdown-item-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            ${icon}
          </svg>
        ` : ''}
        <span>${this.escapeHtml(label)}</span>
      </button>
    `;
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    if (!container) return;

    // Breadcrumb handlers
    this.breadcrumb.attachEventHandlers(container);

    // Action button handlers
    const actionButtons = container.querySelectorAll('[data-action]');
    actionButtons.forEach(button => {
      button.addEventListener('click', (e) => {
        e.preventDefault();
        const action = button.getAttribute('data-action');

        // Check if it's a dropdown toggle
        if (button.hasAttribute('data-dropdown-toggle')) {
          this.toggleDropdown(button);
        } else {
          // Emit action event
          this.eventBus.emit('page-header-action', action);
        }
      });
    });

    // Close dropdowns when clicking outside
    document.addEventListener('click', (e) => {
      if (!e.target.closest('.page-header-action-wrapper')) {
        this.closeAllDropdowns(container);
      }
    });
  }

  /**
   * Toggle dropdown menu
   */
  toggleDropdown(button) {
    const wrapper = button.closest('.page-header-action-wrapper');
    if (!wrapper) return;

    const dropdown = wrapper.querySelector('[data-dropdown-menu]');
    if (!dropdown) return;

    const isOpen = dropdown.classList.contains('open');

    // Close all other dropdowns first
    this.closeAllDropdowns(button.closest('.page-header'));

    // Toggle this dropdown
    if (!isOpen) {
      dropdown.classList.add('open');
      button.setAttribute('aria-expanded', 'true');
    } else {
      dropdown.classList.remove('open');
      button.setAttribute('aria-expanded', 'false');
    }
  }

  /**
   * Close all dropdowns
   */
  closeAllDropdowns(container) {
    const dropdowns = container.querySelectorAll('[data-dropdown-menu]');
    dropdowns.forEach(dropdown => {
      dropdown.classList.remove('open');
      const toggle = dropdown.closest('.page-header-action-wrapper')
        ?.querySelector('[data-dropdown-toggle]');
      if (toggle) {
        toggle.setAttribute('aria-expanded', 'false');
      }
    });
  }

  /**
   * Static helper: Create from route
   */
  static fromRoute(router, eventBus, options = {}) {
    const header = new PageHeader(router, eventBus);
    const path = router.getCurrentPath();
    const params = router.getCurrentParams();

    // Auto-generate breadcrumbs
    const breadcrumbs = [];

    // Home
    breadcrumbs.push({
      label: 'Home',
      path: '#/',
      icon: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>'
    });

    // Parse path and build breadcrumbs
    if (path && path !== '' && path !== '/') {
      const segments = path.split('/').filter(s => s);

      if (segments.length > 0) {
        const first = segments[0];

        if (first === 'analyses') {
          breadcrumbs.push({ label: 'Analyses', path: '#/analyses' });
          if (segments.length > 1 && params.pipelineId) {
            breadcrumbs.push({
              label: params.pipelineName || 'Analysis',
              path: `#/analyses/${params.pipelineId}`
            });
          }
        } else if (first === 'analysis-types') {
          breadcrumbs.push({ label: 'Analysis Types', path: '#/analysis-types' });
          if (segments.length > 1 && params.id) {
            breadcrumbs.push({
              label: params.name || 'Type',
              path: `#/analysis-types/${params.id}`
            });
          }
        } else if (first === 'source-types') {
          breadcrumbs.push({ label: 'Source Types', path: '#/source-types' });
          if (segments.length > 1 && params.id) {
            breadcrumbs.push({
              label: params.name || 'Type',
              path: `#/source-types/${params.id}`
            });
          }
        }
      }
    }

    return header.render({
      ...options,
      breadcrumbs: options.breadcrumbs || breadcrumbs
    });
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
