/**
 * S6.SnapVault Professional Edition
 * Main Application Entry Point
 * Version 1.2 - Professional View Presets with Smart Resolution
 */

import { PhotoGrid } from './components/grid.js';
import { SearchBar } from './components/search.js';
import { Lightbox } from './components/lightbox.js';
import { UploadModal } from './components/upload.js';
import { ProcessMonitor } from './components/processMonitor.js';
import { Timeline } from './components/timeline.js';
import { KeyboardShortcuts } from './components/keyboard.js';
import { BulkActions } from './components/bulkActions.js';
import { ContextPanel } from './components/contextPanel.js';
import { Toast } from './components/toast.js';
import { CollectionsSidebar } from './components/collectionsSidebar.js';
import { PhotoSelection } from './components/photoSelection.js';
import { DragDropManager } from './components/dragDropManager.js';
import { CollectionView } from './components/collectionView.js';
import { API } from './api.js';
import { VIEW_PRESETS, migrateOldDensity } from './viewPresets.js';
import { escapeHtml } from './utils/html.js';
import { StateManager } from './utils/StateManager.js';
import { EventBus } from './utils/EventBus.js';
import { ActionExecutor } from './system/ActionExecutor.js';

class SnapVaultApp {
  constructor() {
    this.currentWorkspace = 'gallery';
    this.components = {};

    // Initialize centralized state management
    this.stateManager = new StateManager();
    this.eventBus = new EventBus();

    // Migrate old density preference to new view preset
    this.migrateUserPreferences();

    // Set initial view preset
    this.stateManager.set('viewPreset', this.loadViewPreset());

    // Backward compatibility: expose state for components that still use it
    this.state = this.stateManager.state;
  }

  migrateUserPreferences() {
    // Check for old density setting and migrate
    const oldDensity = localStorage.getItem('snapvault-density');
    if (oldDensity && !localStorage.getItem('snapvault-view-preset')) {
      const newPreset = migrateOldDensity(parseInt(oldDensity));
      localStorage.setItem('snapvault-view-preset', newPreset);
      localStorage.removeItem('snapvault-density');
      console.log(`[Migration] Migrated density ${oldDensity} to preset "${newPreset}"`);
    }
  }

  loadViewPreset() {
    // Load saved preference or default to 'comfortable'
    const saved = localStorage.getItem('snapvault-view-preset');
    return (saved && VIEW_PRESETS[saved]) ? saved : 'comfortable';
  }

  // Expose photos array for components
  get photos() {
    return this.state.photos;
  }

  async init() {
    console.log('[App] Initializing SnapVault Pro...');

    // Initialize API client
    this.api = new API();

    // Initialize action system (PHASE 2)
    this.actions = new ActionExecutor(this);

    // Initialize components
    this.components.toast = new Toast();
    this.components.grid = new PhotoGrid(this);
    this.components.search = new SearchBar(this);
    this.components.lightbox = new Lightbox(this);
    this.components.upload = new UploadModal(this);
    this.components.processMonitor = new ProcessMonitor(this);
    this.components.timeline = new Timeline(this);
    this.components.keyboard = new KeyboardShortcuts(this);
    this.components.bulkActions = new BulkActions(this);
    this.components.contextPanel = new ContextPanel(this);

    // Collection management components
    this.components.collectionsSidebar = new CollectionsSidebar(this);
    this.components.photoSelection = new PhotoSelection(this);
    this.components.dragDrop = new DragDropManager(this);
    this.components.collectionView = new CollectionView(this);

    // Setup event listeners
    this.setupWorkspaceNavigation();
    this.setupViewPresetControls(); // NEW: View preset controls
    this.setupUploadButtons();
    this.setupLibraryNavigation();
    this.setupDragAndDrop();

    // Load initial data
    await this.loadPhotos();
    await this.loadEvents();

    // Initialize collection management
    await this.components.collectionsSidebar.init();
    this.components.photoSelection.init();
    this.components.dragDrop.init();
    await this.components.collectionView.setView('all-photos');

    // Enable infinite scroll after initial load
    this.components.grid.enableInfiniteScroll();

    console.log('[App] SnapVault Pro ready');
  }

  setupWorkspaceNavigation() {
    const workspaceButtons = document.querySelectorAll('.workspace-btn');
    workspaceButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const workspace = btn.dataset.workspace;
        this.switchWorkspace(workspace);
      });
    });
  }

  switchWorkspace(workspace) {
    // Update active workspace button
    document.querySelectorAll('.workspace-btn').forEach(btn => {
      btn.classList.toggle('active', btn.dataset.workspace === workspace);
    });

    // Update active workspace content
    document.querySelectorAll('.workspace').forEach(ws => {
      ws.classList.toggle('active', ws.dataset.workspace === workspace);
    });

    this.currentWorkspace = workspace;

    // Update components
    if (workspace === 'timeline') {
      this.components.timeline.render();
    }
  }

  setupViewPresetControls() {
    const presetButtons = document.querySelectorAll('.view-preset-btn');
    presetButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const preset = btn.dataset.preset;
        this.setViewPreset(preset);
      });
    });
  }

  async setViewPreset(presetId) {
    console.log(`[setViewPreset] START - switching to ${presetId}`);

    if (!VIEW_PRESETS[presetId]) {
      console.warn(`Unknown view preset: ${presetId}`);
      return;
    }

    // Disable infinite scroll during view preset switch
    this.components.grid.disableInfiniteScroll();

    this.state.viewPreset = presetId;

    // Save preference
    localStorage.setItem('snapvault-view-preset', presetId);

    // Update active button
    document.querySelectorAll('.view-preset-btn').forEach(btn => {
      btn.classList.toggle('active', btn.dataset.preset === presetId);
    });

    console.log(`[setViewPreset] View preset: ${VIEW_PRESETS[presetId].label}`);

    // Update preset attribute only (instant, no image reload)
    this.components.grid.updatePreset();

    // Delay re-enabling infinite scroll to prevent immediate trigger
    // This gives the layout time to settle and user time to scroll
    setTimeout(() => {
      this.components.grid.enableInfiniteScroll();
      console.log(`[setViewPreset] Infinite scroll re-enabled after delay`);
    }, 500);

    console.log(`[setViewPreset] COMPLETE`);
  }

  setupUploadButtons() {
    const uploadButtons = document.querySelectorAll('.btn-upload, .btn-upload-empty');
    uploadButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        this.components.upload.open();
      });
    });
  }

  setupLibraryNavigation() {
    const libraryItems = document.querySelectorAll('.library-section .sidebar-item');
    libraryItems.forEach(item => {
      item.addEventListener('click', () => {
        const label = item.querySelector('.item-label').textContent;

        // Update active state
        libraryItems.forEach(i => i.classList.remove('active'));
        item.classList.add('active');

        // Clear collection active state
        document.querySelectorAll('.collection-item').forEach(c => c.classList.remove('active'));

        // Route to collection view for proper state management
        if (label === 'All Photos') {
          this.components.collectionView.setView('all-photos');
        } else if (label === 'Favorites') {
          this.components.collectionView.setView('favorites');
        }
      });
    });
  }

  async filterPhotos(filter) {
    this.state.currentFilter = filter;

    switch (filter) {
      case 'all':
        await this.loadPhotos();
        break;

      case 'favorites':
        this.setLoading(true);
        // TODO: Backend should support filter=isFavorite:true for server-side filtering
        // For now, load first page and filter client-side
        const allPhotos = await this.api.get('/api/photos?sort=-id&page=1&pageSize=200');
        this.state.photos = allPhotos.filter(p => p.isFavorite);
        this.state.currentPage = 1;
        this.state.hasMorePages = false; // Disable infinite scroll for filtered views
        this.components.grid.render();
        this.components.grid.disableInfiniteScroll();
        this.setLoading(false);
        break;
    }
  }

  async loadPhotos(filterQuery = '') {
    try {
      this.setLoading(true);
      // Initial load: first page only (30 photos for fast FCP)
      // Sort by ID descending (newest first - GUID v7 embeds timestamp)
      const baseQuery = '/api/photos?sort=-id&page=1&pageSize=30';
      const query = filterQuery ? `${baseQuery}&${filterQuery.substring(1)}` : baseQuery;

      const response = await this.api.get(query, {}, { includeHeaders: true });
      this.state.photos = response.data || [];
      this.state.totalPhotosCount = response.headers.totalCount;
      this.state.currentPage = 1;
      this.state.hasMorePages = true; // Assume more until proven otherwise
      this.state.activeFilter = filterQuery; // Store active filter for infinite scroll
      this.components.grid.render();
      this.updateLibraryCounts();
      this.updateStatusBar();

      console.log(`[Photos] Initial load: ${response.data?.length || 0} photos (page 1), total: ${this.state.totalPhotosCount}`);

      // Auto-fill viewport if grid doesn't fill the visible area
      // Particularly important for compact modes on large screens (4K)
      await this.fillViewport();

      // Note: Infinite scroll is enabled by caller (init or setViewPreset)
      // This prevents immediate trigger when layout changes
    } catch (error) {
      console.error('Failed to load photos:', error);
      this.components.toast.show('Failed to load photos', { icon: '⚠️', duration: 5000 });
    } finally {
      this.setLoading(false);
    }
  }

  async loadMorePhotos() {
    if (!this.state.hasMorePages || this.state.loadingMore) return;

    try {
      this.state.loadingMore = true;
      const nextPage = this.state.currentPage + 1;

      console.log(`[Infinite Scroll] Triggered - Loading page ${nextPage}...`);

      // Include active filter in pagination
      const baseQuery = `/api/photos?sort=-id&page=${nextPage}&pageSize=30`;
      const query = this.state.activeFilter ? `${baseQuery}&${this.state.activeFilter.substring(1)}` : baseQuery;

      const response = await this.api.get(query);

      if (response && response.length > 0) {
        this.state.photos.push(...response);
        this.state.currentPage = nextPage;
        this.components.grid.appendPhotos(response);
        this.updateStatusBar();

        console.log(`[Infinite Scroll] Loaded ${response.length} photos (page ${nextPage}, total loaded: ${this.state.photos.length} of ${this.state.totalPhotosCount})`);
      } else {
        // No more photos
        this.state.hasMorePages = false;
        console.log(`[Infinite Scroll] No more pages - reached end of library`);
      }
    } catch (error) {
      console.error('[Infinite Scroll] Failed to load page:', error);
      this.components.toast.show('Failed to load more photos', { icon: '⚠️', duration: 3000 });
    } finally {
      this.state.loadingMore = false;
    }
  }

  /**
   * Auto-fill viewport with photos on initial load
   * Particularly important for compact grid modes on large screens (4K)
   * Keeps loading pages until viewport is filled or collection end is reached
   */
  async fillViewport() {
    const MAX_ITERATIONS = 10; // Safety limit to prevent infinite loops
    let iterations = 0;

    while (iterations < MAX_ITERATIONS) {
      // Check if we need more photos
      if (!this.needsMorePhotosToFillViewport()) {
        console.log('[Viewport Fill] Grid fills viewport - stopping auto-pagination');
        break;
      }

      // Check if more pages are available
      if (!this.state.hasMorePages) {
        console.log('[Viewport Fill] No more pages available - collection end reached');
        break;
      }

      // Load next page
      console.log(`[Viewport Fill] Loading page ${this.state.currentPage + 1} to fill viewport...`);
      await this.loadMorePhotos();

      // Small delay to let DOM update and images load
      await new Promise(resolve => setTimeout(resolve, 100));

      iterations++;
    }

    if (iterations > 0) {
      console.log(`[Viewport Fill] Completed after ${iterations} additional page(s)`);
    }
  }

  /**
   * Check if grid needs more photos to fill the viewport
   * Returns true if grid bottom is above viewport bottom (needs more content)
   */
  needsMorePhotosToFillViewport() {
    const gridContainer = document.querySelector('.photo-grid');
    if (!gridContainer) return false;

    const gridRect = gridContainer.getBoundingClientRect();
    const viewportHeight = window.innerHeight;

    // Check if grid bottom is above viewport bottom (with small buffer)
    const needsMore = gridRect.bottom < (viewportHeight - 100);

    if (needsMore) {
      console.log('[Viewport Fill] Check: Grid bottom at', Math.round(gridRect.bottom), 'px, viewport height', viewportHeight, 'px - needs more');
    }

    return needsMore;
  }

  async loadEvents() {
    try {
      const response = await this.api.get('/api/events');
      this.state.events = response || [];
      this.renderEvents();
    } catch (error) {
      console.error('Failed to load events:', error);
      this.components.toast.show('Failed to load events', { icon: '⚠️', duration: 3000 });
    }
  }

  setLoading(isLoading) {
    const statusBar = document.querySelector('.status-bar .status-text');
    if (statusBar) {
      if (isLoading) {
        statusBar.textContent = 'Loading...';
      } else {
        this.updateStatusBar();
      }
    }
  }

  updateStatusBar() {
    const statusBar = document.querySelector('.status-bar .status-text');
    if (!statusBar) return;

    const loadedCount = this.state.photos.length;
    const totalCount = this.state.totalPhotosCount;

    if (totalCount > 0 && loadedCount < totalCount) {
      statusBar.textContent = `${loadedCount} out of ${totalCount} photos`;
    } else if (totalCount > 0) {
      statusBar.textContent = `${totalCount} photos`;
    } else {
      statusBar.textContent = `${loadedCount} photos`;
    }
  }

  updateLibraryCounts() {
    const allCount = this.state.photos.length;
    const favoritesCount = this.state.photos.filter(p => p.isFavorite).length;

    document.querySelectorAll('.library-item').forEach(item => {
      const label = item.querySelector('.label').textContent;
      const badge = item.querySelector('.badge');

      if (label === 'All Photos') {
        badge.textContent = allCount;
      } else if (label === 'Favorites') {
        badge.textContent = favoritesCount;
      }
    });
  }

  renderEvents() {
    const container = document.querySelector('.events-list');
    if (!container) return;

    if (this.state.events.length === 0) {
      container.innerHTML = '<p class="empty-state">No events yet</p>';
      return;
    }

    container.innerHTML = this.state.events.map(event => `
      <button class="library-item" data-event-id="${event.id}">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect>
          <line x1="16" y1="2" x2="16" y2="6"></line>
          <line x1="8" y1="2" x2="8" y2="6"></line>
          <line x1="3" y1="10" x2="21" y2="10"></line>
        </svg>
        <span class="label">${escapeHtml(event.name)}</span>
        <span class="badge">${event.photoCount || 0}</span>
      </button>
    `).join('');

    // Attach click handlers to event items
    const eventItems = container.querySelectorAll('.library-item[data-event-id]');
    eventItems.forEach(item => {
      item.addEventListener('click', async () => {
        const eventId = item.dataset.eventId;

        // Update active state
        eventItems.forEach(i => i.classList.remove('active'));
        item.classList.add('active');

        // Clear library panel active state
        document.querySelectorAll('.library-panel .library-item').forEach(i => i.classList.remove('active'));

        // Filter photos by event
        await this.filterPhotosByEvent(eventId);
      });
    });
  }

  async filterPhotosByEvent(eventId) {
    this.state.currentFilter = `event:${eventId}`;

    try {
      this.setLoading(true);

      // Use the existing by-event endpoint from PhotosController
      const response = await this.api.get(`/api/photos/by-event/${eventId}`);
      this.state.photos = response.photos || [];
      this.components.grid.render();

      const event = this.state.events.find(e => e.id === eventId);
      if (event) {
        this.components.toast.show(`Showing ${response.photos.length} photos from "${event.name}"`, {
          icon: '📅',
          duration: 2000
        });
      }
    } catch (error) {
      console.error('Failed to load event photos:', error);
      this.components.toast.show('Failed to load event photos', { icon: '⚠️', duration: 3000 });
    } finally {
      this.setLoading(false);
    }
  }

  async favoritePhoto(photoId) {
    const photo = this.state.photos.find(p => p.id === photoId);
    if (!photo) return;

    // Optimistic UI update
    photo.isFavorite = !photo.isFavorite;
    this.components.grid.updatePhotoCard(photoId, photo);
    this.updateLibraryCounts();

    try {
      await this.api.post(`/api/photos/${photoId}/favorite`);
      this.components.toast.show(photo.isFavorite ? 'Added to favorites' : 'Removed from favorites', {
        icon: '⭐',
        duration: 2000
      });
    } catch (error) {
      // Rollback on failure
      photo.isFavorite = !photo.isFavorite;
      this.components.grid.updatePhotoCard(photoId, photo);
      this.updateLibraryCounts();

      this.components.toast.show('Failed to update favorite', {
        icon: '⚠️',
        duration: 5000,
        actions: [{
          label: 'Retry',
          onClick: () => this.favoritePhoto(photoId)
        }]
      });
    }
  }

  async ratePhoto(photoId, rating) {
    const photo = this.state.photos.find(p => p.id === photoId);
    if (!photo) return;

    const oldRating = photo.rating;

    // Optimistic UI update
    photo.rating = rating;
    this.components.grid.updatePhotoCard(photoId, photo);

    try {
      await this.api.post(`/api/photos/${photoId}/rate`, { rating });
    } catch (error) {
      // Rollback on failure
      photo.rating = oldRating;
      this.components.grid.updatePhotoCard(photoId, photo);

      this.components.toast.show('Failed to update rating', {
        icon: '⚠️',
        duration: 5000
      });
    }
  }

  clearSelection() {
    // Clear all selected photos
    this.state.selectedPhotos.forEach(photoId => {
      const card = this.components.grid.photoCards.get(photoId);
      if (card) {
        card.classList.remove('selected');
        card.querySelector('.selection-indicator').style.display = 'none';
      }
    });

    this.stateManager.clearSelection();
    this.components.bulkActions.update(0);
    this.eventBus.emit('selection:cleared');
  }

  setupDragAndDrop() {
    const mainContent = document.querySelector('.main-content');
    if (!mainContent) return;

    let dragCounter = 0;

    // Prevent default drag behavior on entire page
    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
      document.body.addEventListener(eventName, (e) => {
        e.preventDefault();
        e.stopPropagation();
      });
    });

    // Drag enter - show drop zone
    mainContent.addEventListener('dragenter', (e) => {
      dragCounter++;
      // Only show file upload interface for EXTERNAL file drags
      // Internal photo drags have 'text/plain' data (photo ID)
      const isInternalPhotoDrag = e.dataTransfer.types.includes('text/plain');
      const isExternalFileDrag = e.dataTransfer.types.includes('Files') && !isInternalPhotoDrag;

      if (isExternalFileDrag) {
        mainContent.classList.add('drag-over');
      }
    });

    // Drag over - keep drop zone visible
    mainContent.addEventListener('dragover', (e) => {
      // Only show file upload interface for EXTERNAL file drags
      const isInternalPhotoDrag = e.dataTransfer.types.includes('text/plain');
      const isExternalFileDrag = e.dataTransfer.types.includes('Files') && !isInternalPhotoDrag;

      if (isExternalFileDrag) {
        e.dataTransfer.dropEffect = 'copy';
        mainContent.classList.add('drag-over');
      }
    });

    // Drag leave - hide drop zone
    mainContent.addEventListener('dragleave', () => {
      dragCounter--;
      if (dragCounter === 0) {
        mainContent.classList.remove('drag-over');
      }
    });

    // Drop - handle files
    mainContent.addEventListener('drop', (e) => {
      dragCounter = 0;
      mainContent.classList.remove('drag-over');

      // Only handle EXTERNAL file drops (not internal photo drags)
      const isInternalPhotoDrag = e.dataTransfer.types.includes('text/plain');
      const files = e.dataTransfer.files;

      if (files.length > 0 && !isInternalPhotoDrag) {
        // Open upload modal with pre-selected files
        this.components.upload.open(files);
      }
    });
  }

}

// Initialize app when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    window.app = new SnapVaultApp();
    window.app.init();
  });
} else {
  window.app = new SnapVaultApp();
  window.app.init();
}
