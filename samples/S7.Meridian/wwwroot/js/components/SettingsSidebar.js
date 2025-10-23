/**
 * SettingsSidebar - Slide-in configuration panel
 *
 * Design Pattern: Configuration separated from primary work areas
 * Inspiration: Salesforce, HubSpot, GitHub settings patterns
 *
 * Features:
 * - Smooth slide-in animation from right
 * - Backdrop overlay (click to close)
 * - Grouped navigation sections
 * - Keyboard accessible (Esc to close)
 * - Mobile: Full-screen overlay
 */
export class SettingsSidebar {
  constructor(eventBus, router) {
    this.eventBus = eventBus;
    this.router = router;
    this.isOpen = false;
    this.backdropEl = null;
    this.sidebarEl = null;
  }

  /**
   * Render settings sidebar (hidden by default)
   */
  render() {
    return `
      <!-- Backdrop Overlay -->
      <div class="settings-backdrop"
           data-settings-backdrop
           aria-hidden="true"></div>

      <!-- Settings Sidebar -->
      <aside class="settings-sidebar"
             data-settings-sidebar
             role="dialog"
             aria-label="Settings"
             aria-modal="true">

        <!-- Header -->
        <div class="settings-sidebar-header">
          <div class="settings-sidebar-title-group">
            <svg class="settings-sidebar-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="3"></circle>
              <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
            </svg>
            <h2 class="settings-sidebar-title">Settings</h2>
          </div>
          <button class="settings-sidebar-close"
                  data-action="close-settings"
                  aria-label="Close settings">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>

        <!-- Navigation Sections -->
        <nav class="settings-sidebar-nav" aria-label="Settings navigation">

          <!-- Configuration Section -->
          <div class="settings-nav-section">
            <h3 class="settings-nav-section-title">Configuration</h3>
            <div class="settings-nav-links">
              <a href="#/analysis-types"
                 class="settings-nav-link ${this.isActive('analysis-types') ? 'active' : ''}"
                 data-nav="analysis-types"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                  <line x1="12" y1="8" x2="12" y2="16"></line>
                  <line x1="8" y1="12" x2="16" y2="12"></line>
                </svg>
                <span>Analysis Types</span>
                <span class="settings-nav-badge" data-badge="analysis-types-count">0</span>
              </a>

              <a href="#/source-types"
                 class="settings-nav-link ${this.isActive('source-types') ? 'active' : ''}"
                 data-nav="source-types"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                  <polyline points="14 2 14 8 20 8"></polyline>
                  <line x1="12" y1="11" x2="12" y2="17"></line>
                  <polyline points="9 14 12 17 15 14"></polyline>
                </svg>
                <span>Source Types</span>
                <span class="settings-nav-badge" data-badge="source-types-count">0</span>
              </a>

              <a href="#/settings/integration"
                 class="settings-nav-link ${this.isActive('settings/integration') ? 'active' : ''}"
                 data-nav="integration"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="2" y="7" width="20" height="14" rx="2" ry="2"></rect>
                  <path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"></path>
                </svg>
                <span>Integration</span>
                <span class="settings-nav-badge badge-coming-soon">Soon</span>
              </a>
            </div>
          </div>

          <!-- System Section -->
          <div class="settings-nav-section">
            <h3 class="settings-nav-section-title">System</h3>
            <div class="settings-nav-links">
              <a href="#/settings/profile"
                 class="settings-nav-link ${this.isActive('settings/profile') ? 'active' : ''}"
                 data-nav="profile"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
                  <circle cx="12" cy="7" r="4"></circle>
                </svg>
                <span>Profile</span>
              </a>

              <a href="#/settings/api-keys"
                 class="settings-nav-link ${this.isActive('settings/api-keys') ? 'active' : ''}"
                 data-nav="api-keys"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path>
                </svg>
                <span>API Keys</span>
                <span class="settings-nav-badge badge-coming-soon">Soon</span>
              </a>

              <a href="#/settings/audit-log"
                 class="settings-nav-link ${this.isActive('settings/audit-log') ? 'active' : ''}"
                 data-nav="audit-log"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                  <polyline points="14 2 14 8 20 8"></polyline>
                  <line x1="16" y1="13" x2="8" y2="13"></line>
                  <line x1="16" y1="17" x2="8" y2="17"></line>
                  <polyline points="10 9 9 9 8 9"></polyline>
                </svg>
                <span>Audit Log</span>
                <span class="settings-nav-badge badge-coming-soon">Soon</span>
              </a>
            </div>
          </div>

          <!-- Help & Resources Section -->
          <div class="settings-nav-section">
            <h3 class="settings-nav-section-title">Help & Resources</h3>
            <div class="settings-nav-links">
              <a href="#/help/documentation"
                 class="settings-nav-link"
                 data-nav="docs"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"></path>
                  <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"></path>
                </svg>
                <span>Documentation</span>
              </a>

              <a href="#/help/keyboard-shortcuts"
                 class="settings-nav-link"
                 data-nav="shortcuts"
                 data-action="navigate-settings">
                <svg class="settings-nav-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="2" y="4" width="20" height="16" rx="2" ry="2"></rect>
                  <path d="M6 8h.001M10 8h.001M14 8h.001M18 8h.001M8 12h.001M12 12h.001M16 12h.001M7 16h10"></path>
                </svg>
                <span>Keyboard Shortcuts</span>
              </a>
            </div>
          </div>

        </nav>

        <!-- Footer -->
        <div class="settings-sidebar-footer">
          <div class="settings-sidebar-version">
            <span class="text-tertiary text-xs">Meridian v1.0.0</span>
            <span class="text-tertiary text-xs">•</span>
            <span class="text-tertiary text-xs">Koan Framework</span>
          </div>
        </div>

      </aside>
    `;
  }

  /**
   * Check if current path is active
   */
  isActive(path) {
    const currentPath = this.router.getCurrentPath();
    return currentPath.startsWith(path);
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    this.sidebarEl = container.querySelector('[data-settings-sidebar]');
    this.backdropEl = container.querySelector('[data-settings-backdrop]');

    if (!this.sidebarEl || !this.backdropEl) return;

    // Close button
    const closeBtn = this.sidebarEl.querySelector('[data-action="close-settings"]');
    if (closeBtn) {
      closeBtn.addEventListener('click', () => this.close());
    }

    // Backdrop click to close
    this.backdropEl.addEventListener('click', () => this.close());

    // Navigation links
    const navLinks = this.sidebarEl.querySelectorAll('[data-action="navigate-settings"]');
    navLinks.forEach(link => {
      link.addEventListener('click', (e) => {
        const nav = link.getAttribute('data-nav');
        console.log('[SettingsSidebar] Navigate:', nav);
        // Let router handle navigation
        // Optionally close sidebar after navigation (mobile only)
        if (window.innerWidth < 768) {
          setTimeout(() => this.close(), 300);
        }
      });
    });

    // Keyboard shortcuts
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && this.isOpen) {
        this.close();
      }

      // Cmd/Ctrl + K to toggle settings
      if ((e.metaKey || e.ctrlKey) && e.key === ',') {
        e.preventDefault();
        this.toggle();
      }
    });

    // Listen for open settings event
    this.eventBus.on('open-settings', () => this.open());
    this.eventBus.on('close-settings', () => this.close());
  }

  /**
   * Open settings sidebar
   */
  open() {
    if (!this.sidebarEl || !this.backdropEl) return;

    this.isOpen = true;
    this.sidebarEl.classList.add('open');
    this.backdropEl.classList.add('visible');
    document.body.style.overflow = 'hidden'; // Prevent body scroll

    // Focus management
    const firstLink = this.sidebarEl.querySelector('.settings-nav-link');
    if (firstLink) {
      setTimeout(() => firstLink.focus(), 350);
    }

    // Emit event
    this.eventBus.emit('settings-sidebar-opened');
  }

  /**
   * Close settings sidebar
   */
  close() {
    if (!this.sidebarEl || !this.backdropEl) return;

    this.isOpen = false;
    this.sidebarEl.classList.remove('open');
    this.backdropEl.classList.remove('visible');
    document.body.style.overflow = ''; // Restore body scroll

    // Emit event
    this.eventBus.emit('settings-sidebar-closed');
  }

  /**
   * Toggle settings sidebar
   */
  toggle() {
    if (this.isOpen) {
      this.close();
    } else {
      this.open();
    }
  }

  /**
   * Update badge counts
   */
  updateBadges(counts) {
    if (!this.sidebarEl) return;

    if (counts.analysisTypes !== undefined) {
      const badge = this.sidebarEl.querySelector('[data-badge="analysis-types-count"]');
      if (badge) {
        badge.textContent = counts.analysisTypes;
        badge.style.display = counts.analysisTypes > 0 ? 'inline-flex' : 'none';
      }
    }

    if (counts.sourceTypes !== undefined) {
      const badge = this.sidebarEl.querySelector('[data-badge="source-types-count"]');
      if (badge) {
        badge.textContent = counts.sourceTypes;
        badge.style.display = counts.sourceTypes > 0 ? 'inline-flex' : 'none';
      }
    }
  }

  /**
   * Cleanup
   */
  cleanup() {
    document.body.style.overflow = '';
    // Event listeners will be garbage collected with elements
  }
}
