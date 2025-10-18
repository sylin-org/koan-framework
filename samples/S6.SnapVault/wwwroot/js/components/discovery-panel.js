/**
 * Discovery Panel - Intelligent Photo Discovery System
 *
 * Replaces traditional filters with Smart Collections and contextual refinement.
 * Designed for professional photographers managing 50,000+ photo libraries.
 *
 * Performance targets:
 * - Initial load: <100ms
 * - Collection updates: <50ms
 * - Search integration: <300ms
 *
 * See: /docs/DISCOVERY-PANEL-REDESIGN.md
 */

export class DiscoveryPanel {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.sidebar-right .filters-panel');

    this.state = {
      collections: [],
      totalPhotos: 0,
      activeCollection: null,
      isLoading: true,
      context: 'all-photos' // 'all-photos', 'event', 'search-results'
    };

    // Icon mapping (Lucide Icons compatible)
    this.iconMap = {
      'camera': this.createIcon('camera'),
      'alert-circle': this.createIcon('alert-circle'),
      'star': this.createIcon('star'),
      'camera-slr': this.createIcon('camera'),
      'heart': this.createIcon('heart'),
      'calendar': this.createIcon('calendar')
    };
  }

  async init() {
    await this.loadSmartCollections();
    this.render();
    this.attachListeners();
  }

  async loadSmartCollections() {
    try {
      this.state.isLoading = true;
      const response = await this.app.api.get('/api/photos/smart-collections');

      this.state.collections = response.collections || [];
      this.state.totalPhotos = response.totalPhotos || 0;
      this.state.isLoading = false;

      console.log(`Loaded ${this.state.collections.length} Smart Collections (${this.state.totalPhotos} total photos)`);
    } catch (error) {
      console.error('Failed to load Smart Collections:', error);
      this.state.isLoading = false;
    }
  }

  render() {
    if (!this.container) {
      console.warn('Discovery Panel container not found');
      return;
    }

    this.container.innerHTML = `
      <div class="discovery-panel">
        ${this.renderHeader()}
        ${this.renderSearchBar()}
        ${this.state.isLoading ? this.renderSkeleton() : this.renderCollections()}
      </div>
    `;
  }

  renderHeader() {
    return `
      <div class="discovery-header">
        <h3 class="discovery-title">Discover</h3>
        <span class="discovery-count">${this.formatNumber(this.state.totalPhotos)} photos</span>
      </div>
    `;
  }

  renderSearchBar() {
    return `
      <div class="discovery-search">
        <input
          type="search"
          class="discovery-search-input"
          placeholder="Find photos..."
          aria-label="Search photos"
        />
        <div class="discovery-search-icon">
          ${this.createIcon('search')}
        </div>
      </div>
    `;
  }

  renderCollections() {
    if (this.state.collections.length === 0) {
      return this.renderEmptyState();
    }

    return `
      <div class="discovery-collections">
        <div class="discovery-section-header">SMART COLLECTIONS</div>
        ${this.state.collections.map(collection => this.renderCollection(collection)).join('')}
      </div>
    `;
  }

  renderCollection(collection) {
    const isActive = this.state.activeCollection === collection.id;
    const icon = this.iconMap[collection.icon] || this.createIcon('folder');

    return `
      <button
        class="collection-item ${isActive ? 'active' : ''}"
        data-collection-id="${collection.id}"
        data-collection-type="${collection.type}"
        aria-label="${collection.name}, ${collection.photoCount} photos"
        aria-pressed="${isActive}"
      >
        <div class="collection-preview">
          ${this.renderThumbnailGrid(collection.thumbnails)}
        </div>
        <div class="collection-details">
          <div class="collection-header">
            <div class="collection-icon">${icon}</div>
            <h4 class="collection-name">${this.escapeHtml(collection.name)}</h4>
          </div>
          <div class="collection-meta">
            <span class="collection-count">${this.formatNumber(collection.photoCount)} photos</span>
            ${collection.description ? `<span class="collection-description">· ${this.escapeHtml(collection.description)}</span>` : ''}
          </div>
          ${this.renderLastUpdated(collection.lastUpdated)}
        </div>
        <div class="collection-arrow">
          ${this.createIcon('chevron-right')}
        </div>
      </button>
    `;
  }

  renderThumbnailGrid(thumbnails) {
    if (!thumbnails || thumbnails.length === 0) {
      return `
        <div class="thumbnail-grid empty">
          <div class="thumbnail-placeholder"></div>
        </div>
      `;
    }

    // Always show 2x2 grid (4 thumbnails)
    const gridThumbnails = [...thumbnails];
    while (gridThumbnails.length < 4) {
      gridThumbnails.push(thumbnails[thumbnails.length - 1] || '');
    }

    return `
      <div class="thumbnail-grid">
        ${gridThumbnails.slice(0, 4).map(url => `
          <div class="thumbnail-item">
            <img
              src="${url}"
              alt=""
              loading="lazy"
              onerror="this.style.display='none'"
            />
          </div>
        `).join('')}
      </div>
    `;
  }

  renderLastUpdated(lastUpdated) {
    const timeAgo = this.getTimeAgo(new Date(lastUpdated));
    return `<div class="collection-updated">Updated ${timeAgo}</div>`;
  }

  renderEmptyState() {
    return `
      <div class="discovery-empty">
        <div class="discovery-empty-icon">
          ${this.createIcon('image')}
        </div>
        <h4 class="discovery-empty-title">No photos yet</h4>
        <p class="discovery-empty-text">Upload photos to see Smart Collections</p>
      </div>
    `;
  }

  renderSkeleton() {
    return `
      <div class="discovery-collections">
        <div class="discovery-section-header">SMART COLLECTIONS</div>
        ${Array(3).fill(0).map(() => `
          <div class="collection-item skeleton">
            <div class="collection-preview">
              <div class="thumbnail-grid skeleton-grid">
                ${Array(4).fill(0).map(() => '<div class="thumbnail-item skeleton-thumb"></div>').join('')}
              </div>
            </div>
            <div class="collection-details">
              <div class="skeleton-text skeleton-name"></div>
              <div class="skeleton-text skeleton-count"></div>
            </div>
          </div>
        `).join('')}
      </div>
    `;
  }

  attachListeners() {
    // Collection click
    this.container.querySelectorAll('.collection-item:not(.skeleton)').forEach(item => {
      item.addEventListener('click', async (e) => {
        const collectionId = item.dataset.collectionId;
        const collectionType = item.dataset.collectionType;

        console.log(`Activating collection: ${collectionId} (type: ${collectionType})`);

        // Mark as active
        this.state.activeCollection = collectionId;

        // Update UI
        this.container.querySelectorAll('.collection-item').forEach(el => el.classList.remove('active'));
        item.classList.add('active');

        // Apply collection filter
        await this.applyCollectionFilter(collectionId, collectionType);
      });
    });

    // Search input
    const searchInput = this.container.querySelector('.discovery-search-input');
    if (searchInput) {
      let searchTimeout;
      searchInput.addEventListener('input', (e) => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(() => {
          this.handleSearch(e.target.value);
        }, 300); // Debounce 300ms
      });

      searchInput.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
          e.target.value = '';
          e.target.blur();
          this.clearSearch();
        }
      });
    }
  }

  async applyCollectionFilter(collectionId, collectionType) {
    try {
      let filter = {};

      // Build filter based on collection type
      switch (collectionId) {
        case 'recent-uploads':
          const sevenDaysAgo = new Date();
          sevenDaysAgo.setDate(sevenDaysAgo.getDate() - 7);
          filter = {
            uploadedAt: { $gte: sevenDaysAgo.toISOString() }
          };
          break;

        case 'needs-attention':
          const thirtyDaysAgo = new Date();
          thirtyDaysAgo.setDate(thirtyDaysAgo.getDate() - 30);
          filter = {
            uploadedAt: { $lt: thirtyDaysAgo.toISOString() },
            $or: [
              { rating: 0 },
              { autoTags: { $size: 0 } }
            ]
          };
          break;

        case 'this-weeks-best':
        case 'last-months-best':
          const daysAgo = collectionId === 'this-weeks-best' ? 7 : 30;
          const dateThreshold = new Date();
          dateThreshold.setDate(dateThreshold.getDate() - daysAgo);
          filter = {
            rating: { $gte: 4 },
            uploadedAt: { $gte: dateThreshold.toISOString() }
          };
          break;

        case 'favorites':
          filter = { isFavorite: true };
          break;

        default:
          // Camera profile
          if (collectionType === 'camera') {
            const collection = this.state.collections.find(c => c.id === collectionId);
            if (collection) {
              filter = { cameraModel: collection.name };
            }
          }
      }

      // Apply filter to gallery
      console.log('Applying filter:', filter);

      // Build filter query string
      const filterQuery = Object.keys(filter).length > 0
        ? `?filter=${encodeURIComponent(JSON.stringify(filter))}`
        : '';

      // Reload gallery with filter
      await this.app.loadPhotos(filterQuery);

    } catch (error) {
      console.error('Failed to apply collection filter:', error);
      this.app.showToast('Failed to load collection', 'error');
    }
  }

  async handleSearch(query) {
    if (!query.trim()) {
      this.clearSearch();
      return;
    }

    console.log('Searching:', query);
    this.state.context = 'search-results';

    // Trigger semantic search
    // TODO: Implement semantic search integration
    this.app.showToast('Search coming soon', 'info');
  }

  clearSearch() {
    this.state.context = 'all-photos';
    this.state.activeCollection = null;

    // Reset gallery
    this.app.loadPhotos();

    // Update UI
    this.container.querySelectorAll('.collection-item').forEach(el => el.classList.remove('active'));
  }

  // Utility: Create SVG icon
  createIcon(name) {
    const icons = {
      'search': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>',
      'camera': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/></svg>',
      'alert-circle': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>',
      'star': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>',
      'heart': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"/></svg>',
      'calendar': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>',
      'folder': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>',
      'image': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>',
      'chevron-right': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>'
    };

    return icons[name] || icons['folder'];
  }

  // Utility: Format number with commas
  formatNumber(num) {
    return num.toLocaleString();
  }

  // Utility: Get time ago string
  getTimeAgo(date) {
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;
    if (diffDays < 30) return `${Math.floor(diffDays / 7)}w ago`;
    return `${Math.floor(diffDays / 30)}mo ago`;
  }

  // Utility: Escape HTML
  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  // Public API: Refresh collections
  async refresh() {
    await this.loadSmartCollections();
    this.render();
    this.attachListeners();
  }

  // Public API: Set context (for contextual adaptation)
  setContext(context) {
    this.state.context = context;
    // TODO: Re-render with context-specific collections
  }
}
