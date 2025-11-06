/**
 * Breadcrumb - Contextual navigation component
 *
 * Design Pattern: Show user's location in hierarchy
 * Features:
 * - Auto-generated from route
 * - Click to navigate back
 * - Mobile: Shows only back button + current
 * - Semantic HTML with aria-label
 */
export class Breadcrumb {
  constructor(router, eventBus) {
    this.router = router;
    this.eventBus = eventBus;
  }

  /**
   * Render breadcrumb navigation
   * @param {Array} crumbs - Array of {label, path, icon?} objects
   * @param {Object} options - {showBackButton, mobileCompact}
   * @returns {string} HTML string
   */
  render(crumbs, options = {}) {
    const {
      showBackButton = true,
      mobileCompact = true
    } = options;

    if (!crumbs || crumbs.length === 0) {
      return '';
    }

    const backPath = crumbs.length > 1 ? crumbs[crumbs.length - 2].path : crumbs[0].path;

    return `
      <nav class="breadcrumb" aria-label="Breadcrumb">
        ${showBackButton ? `
          <button class="breadcrumb-back-btn"
                  data-action="breadcrumb-back"
                  data-path="${this.escapeHtml(backPath)}"
                  aria-label="Go back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="15 18 9 12 15 6"></polyline>
            </svg>
          </button>
        ` : ''}

        <ol class="breadcrumb-list ${mobileCompact ? 'mobile-compact' : ''}">
          ${crumbs.map((crumb, index) => this.renderCrumb(crumb, index, crumbs.length)).join('')}
        </ol>
      </nav>
    `;
  }

  /**
   * Render individual breadcrumb item
   */
  renderCrumb(crumb, index, total) {
    const isLast = index === total - 1;
    const isMobileHidden = index < total - 2; // Hide all except last 2 on mobile

    return `
      <li class="breadcrumb-item ${isMobileHidden ? 'mobile-hidden' : ''} ${isLast ? 'breadcrumb-current' : ''}">
        ${!isLast ? `
          <a href="${this.escapeHtml(crumb.path)}"
             class="breadcrumb-link"
             data-breadcrumb-nav="${this.escapeHtml(crumb.path)}">
            ${crumb.icon ? `
              <svg class="breadcrumb-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                ${crumb.icon}
              </svg>
            ` : ''}
            <span>${this.escapeHtml(crumb.label)}</span>
          </a>
        ` : `
          <span class="breadcrumb-current-text">
            ${crumb.icon ? `
              <svg class="breadcrumb-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                ${crumb.icon}
              </svg>
            ` : ''}
            <span>${this.escapeHtml(crumb.label)}</span>
          </span>
        `}

        ${!isLast ? `
          <svg class="breadcrumb-separator" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="9 18 15 12 9 6"></polyline>
          </svg>
        ` : ''}
      </li>
    `;
  }

  /**
   * Auto-generate breadcrumbs from route
   */
  static fromRoute(router, eventBus) {
    const breadcrumb = new Breadcrumb(router, eventBus);
    const path = router.getCurrentPath();
    const params = router.getCurrentParams();

    const crumbs = [];

    // Always start with home
    crumbs.push({
      label: 'Home',
      path: '#/',
      icon: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>'
    });

    // Parse path segments
    if (path && path !== '' && path !== '/') {
      const segments = path.split('/').filter(s => s);

      if (segments.length > 0) {
        const first = segments[0];

        // Primary areas
        if (first === 'analyses') {
          crumbs.push({
            label: 'Analyses',
            path: '#/analyses'
          });

          // Specific analysis
          if (segments.length > 1 && params.pipelineId) {
            crumbs.push({
              label: params.pipelineName || 'Analysis',
              path: `#/analyses/${params.pipelineId}`
            });
          }
        }

        // Analysis Types (Settings)
        else if (first === 'analysis-types') {
          crumbs.push({
            label: 'Analysis Types',
            path: '#/analysis-types'
          });

          // Specific type view/edit
          if (segments.length > 1 && params.id) {
            const mode = segments[segments.length - 1]; // 'view' or 'edit'
            crumbs.push({
              label: params.name || 'Analysis Type',
              path: `#/analysis-types/${params.id}`
            });

            if (mode === 'edit') {
              crumbs.push({
                label: 'Edit',
                path: `#/analysis-types/${params.id}/edit`
              });
            }
          }
        }

        // Source Types (Settings)
        else if (first === 'source-types') {
          crumbs.push({
            label: 'Source Types',
            path: '#/source-types'
          });

          // Specific type view/edit
          if (segments.length > 1 && params.id) {
            const mode = segments[segments.length - 1];
            crumbs.push({
              label: params.name || 'Source Type',
              path: `#/source-types/${params.id}`
            });

            if (mode === 'edit') {
              crumbs.push({
                label: 'Edit',
                path: `#/source-types/${params.id}/edit`
              });
            }
          }
        }

        // Settings pages
        else if (first === 'settings') {
          crumbs.push({
            label: 'Settings',
            path: '#/settings'
          });

          if (segments.length > 1) {
            const settingsPage = segments[1];
            const label = settingsPage
              .split('-')
              .map(word => word.charAt(0).toUpperCase() + word.slice(1))
              .join(' ');
            crumbs.push({
              label,
              path: `#/settings/${settingsPage}`
            });
          }
        }

        // Help pages
        else if (first === 'help') {
          crumbs.push({
            label: 'Help',
            path: '#/help'
          });

          if (segments.length > 1) {
            const helpPage = segments[1];
            const label = helpPage
              .split('-')
              .map(word => word.charAt(0).toUpperCase() + word.slice(1))
              .join(' ');
            crumbs.push({
              label,
              path: `#/help/${helpPage}`
            });
          }
        }
      }
    }

    return breadcrumb.render(crumbs);
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    // Back button
    const backBtn = container.querySelector('[data-action="breadcrumb-back"]');
    if (backBtn) {
      backBtn.addEventListener('click', (e) => {
        e.preventDefault();
        const path = backBtn.getAttribute('data-path');
        if (path) {
          window.location.hash = path;
        } else {
          window.history.back();
        }
      });
    }

    // Breadcrumb links (let router handle via href)
    const links = container.querySelectorAll('[data-breadcrumb-nav]');
    links.forEach(link => {
      link.addEventListener('click', (e) => {
        // Let href handle navigation
        console.log('[Breadcrumb] Navigate via href');
      });
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
