/**
 * Sidebar Navigation Component
 * SnapVault-inspired navigation rail with semantic sections
 */

export class Sidebar {
  constructor(router, eventBus) {
    this.router = router;
    this.eventBus = eventBus;
    this.sections = this.buildSections();
    this.container = null;
    this.activeItemId = null;

    this.eventBus.on('sidebar:set-badge', ({ id, value }) => {
      this.setBadge(id, value);
    });

    this.eventBus.on('sidebar:update-badges', (badgeMap) => {
      this.updateBadges(badgeMap);
    });
  }

  buildSections() {
    const icons = {
      grid: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>',
      star: '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26"></polygon>',
      clock: '<circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline>',
      activity: '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline>',
      trending: '<polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 9 12 13 16 23 6"></polyline>',
      fileText: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line>',
      layers: '<polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline>',
      zap: '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>',
      folder: '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>',
      bookmark: '<path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path>',
    };

    return [
      {
        header: 'Library',
        items: [
          {
            id: 'all-analyses',
            label: 'All Analyses',
            icon: icons.grid,
            view: 'analyses-list',
            shortcut: ['G', 'A'],
          },
          {
            id: 'favorites',
            label: 'Favorites',
            icon: icons.star,
            view: 'analyses-favorites',
            shortcut: ['G', 'F'],
          },
          {
            id: 'recent',
            label: 'Recent',
            icon: icons.clock,
            view: 'analyses-recent',
            shortcut: ['G', 'R'],
          },
        ],
      },
      {
        header: 'Work',
        items: [
          {
            id: 'active-analyses',
            label: 'Active Pipelines',
            icon: icons.activity,
            view: 'analyses-active',
          },
          {
            id: 'insights-dashboard',
            label: 'Insights Dashboard',
            icon: icons.trending,
            view: 'dashboard',
          },
          {
            id: 'document-library',
            label: 'Document Library',
            icon: icons.folder,
            view: 'documents-library',
          },
        ],
      },
      {
        header: 'Configuration',
        items: [
          {
            id: 'analysis-types',
            label: 'Analysis Types',
            icon: icons.layers,
            view: 'analysis-types-list',
            shortcut: ['G', 'T'],
          },
          {
            id: 'source-types',
            label: 'Source Types',
            icon: icons.fileText,
            view: 'source-types-list',
          },
          {
            id: 'integrations',
            label: 'Integrations',
            icon: icons.zap,
            view: 'integrations',
          },
        ],
      },
    ];
  }

  mount(container) {
    if (!container) return;
    this.container = container;
    this.container.classList.add('app-sidebar');
    this.container.innerHTML = this.render();
    this.attachEventHandlers();
    this.setActiveFromPath(this.router.getCurrentPath());
  }

  render() {
    return `
      <div class="sidebar-mobile-header">
        <span class="sidebar-title">Workspace</span>
        <button type="button" class="sidebar-close" data-sidebar-close aria-label="Close navigation">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>
      ${this.sections.map((section) => this.renderSection(section)).join('')}
    `;
  }

  renderSection(section) {
    return `
      <nav class="sidebar-section" aria-label="${section.header}">
        <h2 class="section-header">${section.header}</h2>
        ${section.items.map((item) => this.renderItem(item)).join('')}
      </nav>
    `;
  }

  renderItem(item) {
    const shortcut = item.shortcut
      ? `<span class="shortcut">${item.shortcut.join(' · ')}</span>`
      : '';

    return `
      <button
        class="sidebar-item"
        type="button"
        data-item-id="${item.id}"
        data-view="${item.view}"
        ${item.shortcut ? `data-shortcut="${item.shortcut.join('+')}"` : ''}
      >
        <svg class="item-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
          ${item.icon}
        </svg>
        <span class="item-label">${item.label}</span>
        <span class="item-meta">
          <span class="item-badge" data-badge-for="${item.id}" hidden>0</span>
          ${shortcut}
        </span>
      </button>
    `;
  }

  attachEventHandlers() {
    if (!this.container) return;

    this.container.addEventListener('click', (event) => {
      const button = event.target.closest('.sidebar-item');
      if (!button) return;

      const view = button.dataset.view;
      const itemId = button.dataset.itemId;

      if (!view) return;

      this.activeItemId = itemId;
      this.updateActiveStyles();

      // Bubble navigation through existing app contract
      const resolvedView = this.resolveView(view);
      this.eventBus.emit('navigate', resolvedView);

      if (window.matchMedia('(max-width: 1024px)').matches) {
        this.close();
      }
    });

    const closeButton = this.container.querySelector('[data-sidebar-close]');
    if (closeButton) {
      closeButton.addEventListener('click', () => this.close());
    }
  }

  resolveView(view) {
    const map = {
      'insights-dashboard': 'dashboard',
      'analyses-favorites': 'analyses-list',
      'analyses-recent': 'analyses-list',
      'analyses-active': 'analyses-list',
      'document-library': 'analyses-list',
      integrations: 'dashboard'
    };

    return map[view] || view;
  }

  updateBadges(badgeMap = {}) {
    Object.entries(badgeMap).forEach(([id, value]) => this.setBadge(id, value));
  }

  setBadge(id, value) {
    if (!this.container) return;
    const badge = this.container.querySelector(`[data-badge-for="${id}"]`);
    if (!badge) return;

    if (value === null || value === undefined || value === 0) {
      badge.hidden = true;
      badge.textContent = '0';
    } else {
      badge.hidden = false;
      badge.textContent = value > 99 ? '99+' : value;
    }
  }

  setActiveFromPath(path) {
    if (!path) {
      this.activeItemId = 'insights-dashboard';
      this.updateActiveStyles();
      return;
    }

    const normalized = path.replace(/^#/,'').replace(/^\//,'');

    if (!normalized || normalized === 'dashboard') {
      this.activeItemId = 'insights-dashboard';
    } else if (normalized.startsWith('analyses')) {
      this.activeItemId = 'all-analyses';
    } else if (normalized.startsWith('analysis-types')) {
      this.activeItemId = 'analysis-types';
    } else if (normalized.startsWith('source-types')) {
      this.activeItemId = 'source-types';
    }

    this.updateActiveStyles();
  }

  setActiveByView(view) {
    switch (view) {
      case 'dashboard':
        this.activeItemId = 'insights-dashboard';
        break;
      case 'analysis-types-list':
      case 'analysis-type-view':
      case 'analysis-type-create':
      case 'analysis-type-edit':
        this.activeItemId = 'analysis-types';
        break;
      case 'source-types-list':
      case 'source-type-view':
      case 'source-type-create':
      case 'source-type-edit':
        this.activeItemId = 'source-types';
        break;
      case 'analyses-list':
      case 'analysis-workspace':
        this.activeItemId = 'all-analyses';
        break;
      default:
        break;
    }

    this.updateActiveStyles();
  }

  updateActiveStyles() {
    if (!this.container) return;

    this.container.querySelectorAll('.sidebar-item').forEach((item) => {
      const itemId = item.dataset.itemId;
      item.classList.toggle('active', itemId === this.activeItemId);
    });
  }

  isOpen() {
    return this.container?.classList.contains('open');
  }

  open() {
    if (!this.container) return;
    this.container.classList.add('open');
    document.body.style.overflow = 'hidden';
  }

  close() {
    if (!this.container) return;
    this.container.classList.remove('open');
    document.body.style.overflow = '';
  }
}
