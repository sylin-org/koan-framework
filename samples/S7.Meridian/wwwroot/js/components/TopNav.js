/**
 * TopNav - Persistent top navigation menu
 * Provides consistent navigation across all application views
 */
export class TopNav {
  constructor(router, eventBus) {
    this.router = router;
    this.eventBus = eventBus;
  }

  /**
   * Render the top navigation bar
   * @returns {string} HTML content
   */
  render() {
    const currentPath = this.router.getCurrentPath();

    return `
      <nav class="top-nav">
        <div class="top-nav-container">
          <!-- Logo/Brand -->
          <div class="top-nav-brand">
            <a href="#/" class="brand-link" data-nav="dashboard">
              <svg class="brand-icon" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="3"></circle>
                <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
              </svg>
              <span class="brand-name">Meridian</span>
            </a>
          </div>

          <!-- Main Navigation Links -->
          <div class="top-nav-links">
            <a href="#/"
               class="top-nav-link ${this.isActive('', currentPath) ? 'active' : ''}"
               data-nav="dashboard"
               title="Dashboard">
              <svg class="nav-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="7" height="7"></rect>
                <rect x="14" y="3" width="7" height="7"></rect>
                <rect x="14" y="14" width="7" height="7"></rect>
                <rect x="3" y="14" width="7" height="7"></rect>
              </svg>
              <span class="nav-label">Dashboard</span>
            </a>

            <a href="#/analyses"
               class="top-nav-link ${this.isActive('analyses', currentPath) ? 'active' : ''}"
               data-nav="analyses"
               title="Analyses">
              <svg class="nav-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path>
                <polyline points="13 2 13 9 20 9"></polyline>
              </svg>
              <span class="nav-label">Analyses</span>
            </a>

            <a href="#/analysis-types"
               class="top-nav-link ${this.isActive('analysis-types', currentPath) ? 'active' : ''}"
               data-nav="analysis-types"
               title="Analysis Types">
              <svg class="nav-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"></path>
                <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"></path>
              </svg>
              <span class="nav-label">Analysis Types</span>
            </a>

            <a href="#/source-types"
               class="top-nav-link ${this.isActive('source-types', currentPath) ? 'active' : ''}"
               data-nav="source-types"
               title="Source Types">
              <svg class="nav-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
                <line x1="16" y1="13" x2="8" y2="13"></line>
                <line x1="16" y1="17" x2="8" y2="17"></line>
                <polyline points="10 9 9 9 8 9"></polyline>
              </svg>
              <span class="nav-label">Source Types</span>
            </a>

            <a href="#/organization-profiles"
               class="top-nav-link ${this.isActive('organization-profiles', currentPath) ? 'active' : ''}"
               data-nav="organization-profiles"
               title="Organization Profiles">
              <svg class="nav-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M16 21v-2a4 4 0 0 0-3-3.87"></path>
                <path d="M8 21v-2a4 4 0 0 1 3-3.87"></path>
                <circle cx="12" cy="7" r="4"></circle>
                <path d="M5.5 20.5a4 4 0 0 0-2.5-3.5"></path>
                <path d="M18.5 20.5a4 4 0 0 1 2.5-3.5"></path>
                <circle cx="5" cy="11" r="3"></circle>
                <circle cx="19" cy="11" r="3"></circle>
              </svg>
              <span class="nav-label">Org Profiles</span>
            </a>
          </div>

          <!-- Right Side Actions -->
          <div class="top-nav-actions">
            <button class="top-nav-action-btn top-nav-settings-btn" data-action="open-settings" title="Settings (⌘,)">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="3"></circle>
                <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
              </svg>
            </button>

            <button class="top-nav-action-btn mobile-menu-toggle" data-action="toggle-mobile-menu" title="Menu">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="3" y1="12" x2="21" y2="12"></line>
                <line x1="3" y1="6" x2="21" y2="6"></line>
                <line x1="3" y1="18" x2="21" y2="18"></line>
              </svg>
            </button>
          </div>
        </div>

        <!-- Mobile Menu Overlay -->
        <div class="mobile-menu" data-mobile-menu>
          <div class="mobile-menu-header">
            <span class="mobile-menu-title">Navigation</span>
            <button class="mobile-menu-close" data-action="close-mobile-menu">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>
          <div class="mobile-menu-links">
            <a href="#/" class="mobile-menu-link ${this.isActive('', currentPath) ? 'active' : ''}" data-nav="dashboard">
              <svg class="nav-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="7" height="7"></rect>
                <rect x="14" y="3" width="7" height="7"></rect>
                <rect x="14" y="14" width="7" height="7"></rect>
                <rect x="3" y="14" width="7" height="7"></rect>
              </svg>
              <span>Dashboard</span>
            </a>
            <a href="#/analyses" class="mobile-menu-link ${this.isActive('analyses', currentPath) ? 'active' : ''}" data-nav="analyses">
              <svg class="nav-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path>
                <polyline points="13 2 13 9 20 9"></polyline>
              </svg>
              <span>Analyses</span>
            </a>
            <a href="#/analysis-types" class="mobile-menu-link ${this.isActive('analysis-types', currentPath) ? 'active' : ''}" data-nav="analysis-types">
              <svg class="nav-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"></path>
                <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"></path>
              </svg>
              <span>Analysis Types</span>
            </a>
            <a href="#/source-types" class="mobile-menu-link ${this.isActive('source-types', currentPath) ? 'active' : ''}" data-nav="source-types">
              <svg class="nav-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
                <line x1="16" y1="13" x2="8" y2="13"></line>
                <line x1="16" y1="17" x2="8" y2="17"></line>
                <polyline points="10 9 9 9 8 9"></polyline>
              </svg>
              <span>Source Types</span>
            </a>
            <a href="#/organization-profiles" class="mobile-menu-link ${this.isActive('organization-profiles', currentPath) ? 'active' : ''}" data-nav="organization-profiles">
              <svg class="nav-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M16 21v-2a4 4 0 0 0-3-3.87"></path>
                <path d="M8 21v-2a4 4 0 0 1 3-3.87"></path>
                <circle cx="12" cy="7" r="4"></circle>
                <path d="M5.5 20.5a4 4 0 0 0-2.5-3.5"></path>
                <path d="M18.5 20.5a4 4 0 0 1 2.5-3.5"></path>
                <circle cx="5" cy="11" r="3"></circle>
                <circle cx="19" cy="11" r="3"></circle>
              </svg>
              <span>Org Profiles</span>
            </a>
            <hr class="mobile-menu-divider" />
            <button class="mobile-menu-link" data-action="open-settings-mobile">
              <svg class="nav-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="3"></circle>
                <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
              </svg>
              <span>Settings</span>
            </button>
          </div>
        </div>
      </nav>
    `;
  }

  /**
   * Check if a navigation item is active
   * @param {string} navPath - Navigation path to check
   * @param {string} currentPath - Current router path
   * @returns {boolean} True if active
   */
  isActive(navPath, currentPath) {
    // Remove leading slash from current path
    const cleanPath = currentPath.startsWith('/') ? currentPath.substring(1) : currentPath;

    // Empty path matches dashboard
    if (navPath === '' || navPath === 'dashboard') {
      return cleanPath === '' || cleanPath === 'dashboard';
    }

    // Check if current path starts with nav path
    return cleanPath.startsWith(navPath);
  }

  /**
   * Attach event handlers
   * @param {HTMLElement} container - Container element
   */
  attachEventHandlers(container) {
    if (!container) return;

    const topNav = container.querySelector('.top-nav');
    if (!topNav) return;

    // Navigation link clicks (use default browser behavior for hash links)
    // The router will handle the navigation automatically

    // Settings button
    const settingsBtn = topNav.querySelector('[data-action="open-settings"]');
    if (settingsBtn) {
      settingsBtn.addEventListener('click', (e) => {
        e.preventDefault();
        this.eventBus.emit('open-settings');
      });
    }

    // Mobile settings button
    const settingsBtnMobile = topNav.querySelector('[data-action="open-settings-mobile"]');
    if (settingsBtnMobile) {
      settingsBtnMobile.addEventListener('click', (e) => {
        e.preventDefault();
        // Close mobile menu first
        const mobileMenu = topNav.querySelector('[data-mobile-menu]');
        if (mobileMenu) {
          mobileMenu.classList.remove('open');
          document.body.style.overflow = '';
        }
        // Then open settings
        this.eventBus.emit('open-settings');
      });
    }

    // Mobile menu toggle
    const mobileToggle = topNav.querySelector('[data-action="toggle-mobile-menu"]');
    const mobileMenu = topNav.querySelector('[data-mobile-menu]');
    const mobileClose = topNav.querySelector('[data-action="close-mobile-menu"]');

    if (mobileToggle) {
      mobileToggle.addEventListener('click', (e) => {
        e.preventDefault();
        this.eventBus.emit('toggle-sidebar');
      });
    }

    if (mobileClose && mobileMenu) {
      mobileClose.addEventListener('click', (e) => {
        e.preventDefault();
        mobileMenu.classList.remove('open');
        this.eventBus.emit('close-sidebar');
      });
    }
  }

  /**
   * Show help dialog
   */
  showHelp() {
    // TODO: Implement help modal or redirect to documentation
    alert('Help documentation coming soon!\n\nFor now, explore the dashboard to get started.');
  }

  /**
   * Update active state (called when route changes)
   */
  updateActiveState() {
    const container = document.querySelector('.top-nav');
    if (!container) return;

    const currentPath = this.router.getCurrentPath();

    // Update desktop links
    container.querySelectorAll('.top-nav-link').forEach(link => {
      const navAttr = link.getAttribute('data-nav');
      if (this.isActive(navAttr, currentPath)) {
        link.classList.add('active');
      } else {
        link.classList.remove('active');
      }
    });

    // Update mobile links
    container.querySelectorAll('.mobile-menu-link').forEach(link => {
      const navAttr = link.getAttribute('data-nav');
      if (this.isActive(navAttr, currentPath)) {
        link.classList.add('active');
      } else {
        link.classList.remove('active');
      }
    });
  }
}
