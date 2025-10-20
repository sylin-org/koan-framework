/**
 * CollectionView Component
 * Manages the main content area header (title + view modes)
 * Actions are now handled by ContextPanel in the right sidebar
 *
 * State Management:
 * Single viewState object determines all UI rendering and behavior
 * - { type: 'all-photos' }
 * - { type: 'favorites' }
 * - { type: 'collection', collection: {...} }
 * - { type: 'search', searchQuery: '...', searchAlpha: 0.5 }
 *
 * PhotoSet Integration:
 * CollectionView owns the PhotoSet instance for the current view
 * Grid renders from PhotoSet cache, Lightbox navigates same PhotoSet
 */

import { PhotoSetManager } from '../services/PhotoSetManager.js';

export class CollectionView {
  constructor(app) {
    this.app = app;
    this.viewState = { type: 'all-photos' }; // Single source of truth
    this.photoSet = null; // PhotoSet instance for current view
  }

  /**
   * Set view and load data
   * Single entry point for all view transitions
   * @param {string} viewId - View identifier ('all-photos', 'favorites', collectionId, 'search')
   * @param {object} options - Additional options for specific view types (e.g., search query)
   */
  async setView(viewId, options = {}) {
    console.log(`[CollectionView] Setting view to: ${viewId}`, options);

    // Build new state object based on viewId
    if (viewId === 'all-photos') {
      this.viewState = { type: 'all-photos' };
    } else if (viewId === 'favorites') {
      this.viewState = { type: 'favorites' };
    } else if (viewId === 'search') {
      // Search view with query and alpha from options
      this.viewState = {
        type: 'search',
        searchQuery: options.query || '',
        searchAlpha: options.alpha !== undefined ? options.alpha : 0.5
      };
    } else {
      // It's a collection ID - load the data
      try {
        const collection = await this.app.api.get(`/api/collections/${viewId}`);
        this.viewState = {
          type: 'collection',
          collection: collection
        };
      } catch (error) {
        console.error('[CollectionView] Failed to load collection:', error);
        this.app.components.toast.show('Failed to load collection', {
          icon: '⚠️',
          duration: 3000
        });
        // Fallback to all photos
        this.viewState = { type: 'all-photos' };
      }
    }

    // Update all UI based on new state
    this.updateUI();

    console.log(`[CollectionView] View state:`, this.viewState);
  }

  /**
   * Update all UI components based on current viewState
   */
  updateUI() {
    this.updateRightSidebarVisibility();
    this.renderHeader();
    this.loadPhotos();
  }

  /**
   * Show or hide right sidebar based on current view
   * Collections: show discovery panel for smart collections
   * All Photos/Favorites: hide (not applicable for global views)
   */
  updateRightSidebarVisibility() {
    const rightSidebar = document.querySelector('.sidebar-right');
    if (!rightSidebar) return;

    if (this.viewState.type === 'collection') {
      rightSidebar.style.display = 'block';
    } else {
      rightSidebar.style.display = 'none';
    }
  }

  /**
   * Render context-specific header (title and icon only)
   * Actions are now handled by ContextPanel in right sidebar
   */
  renderHeader() {
    const header = document.querySelector('.content-header');
    if (!header) {
      console.warn('[CollectionView] Content header not found');
      return;
    }

    // Update icon
    const iconElement = header.querySelector('.collection-icon');
    const titleElement = header.querySelector('.page-title');
    if (!titleElement) return;

    // Clear any previous edit handlers
    this.cleanupTitleEditHandlers(titleElement);

    // Render based on view type
    switch (this.viewState.type) {
      case 'all-photos':
        if (iconElement) iconElement.textContent = '📷';
        titleElement.textContent = 'All Photos';
        titleElement.contentEditable = false;
        titleElement.classList.remove('editable');
        break;

      case 'favorites':
        if (iconElement) iconElement.textContent = '⭐';
        titleElement.textContent = 'Favorites';
        titleElement.contentEditable = false;
        titleElement.classList.remove('editable');
        break;

      case 'search':
        if (iconElement) iconElement.textContent = '🔍';
        titleElement.textContent = `Search: "${this.viewState.searchQuery}"`;
        titleElement.contentEditable = false;
        titleElement.classList.remove('editable');
        break;

      case 'collection':
        const { collection } = this.viewState;
        if (iconElement) iconElement.textContent = '📁';
        titleElement.textContent = collection.name;
        titleElement.contentEditable = false;
        titleElement.classList.add('editable');
        titleElement.dataset.collectionId = collection.id;
        titleElement.dataset.originalName = collection.name;
        this.attachTitleEditHandlers(titleElement);
        break;

      default:
        console.warn('[CollectionView] Unknown view type:', this.viewState.type);
    }

    // Update context panel
    if (this.app.components.contextPanel) {
      this.app.components.contextPanel.update();
    }
  }

  /**
   * Attach edit handlers to collection title
   * Click to activate edit mode, then large prominent editing in main content area
   */
  attachTitleEditHandlers(titleElement) {
    const collectionId = titleElement.dataset.collectionId;
    const originalName = titleElement.dataset.originalName;

    // Click event - activate edit mode
    const clickHandler = () => {
      // Only activate if not already editing
      if (titleElement.contentEditable === 'true') return;

      // Activate edit mode
      titleElement.contentEditable = true;

      // Remove emoji prefix for editing
      const textWithoutEmoji = titleElement.textContent.replace('📁 ', '');
      titleElement.textContent = textWithoutEmoji;

      // Focus and select all text
      titleElement.focus();
      setTimeout(() => {
        const range = document.createRange();
        range.selectNodeContents(titleElement);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
      }, 0);
    };

    // Blur event - save changes and deactivate edit mode
    const blurHandler = async () => {
      const newName = titleElement.textContent.trim();

      // Deactivate edit mode
      titleElement.contentEditable = false;

      // Restore emoji prefix
      titleElement.textContent = `📁 ${newName || originalName}`;

      // Only save if name actually changed
      if (newName && newName !== originalName) {
        try {
          await this.app.api.put(`/api/collections/${collectionId}`, {
            name: newName
          });

          // Update state
          titleElement.dataset.originalName = newName;
          if (this.viewState.type === 'collection') {
            this.viewState.collection.name = newName;
          }

          // Reload sidebar to reflect change
          if (this.app.components.collectionsSidebar) {
            await this.app.components.collectionsSidebar.loadCollections();
            this.app.components.collectionsSidebar.render();
          }

          this.app.components.toast.show(`Renamed to "${newName}"`, {
            icon: '✏️',
            duration: 2000
          });
        } catch (error) {
          console.error('[CollectionView] Failed to rename collection:', error);
          titleElement.textContent = `📁 ${originalName}`;
          this.app.components.toast.show('Failed to rename collection', {
            icon: '⚠️',
            duration: 3000
          });
        }
      } else if (!newName) {
        // Empty name - revert
        titleElement.textContent = `📁 ${originalName}`;
      }
    };

    // Keydown event - handle Enter and Escape
    const keydownHandler = (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        titleElement.blur(); // Triggers blurHandler which saves and deactivates
      } else if (e.key === 'Escape') {
        // Deactivate edit mode without saving
        titleElement.contentEditable = false;
        titleElement.textContent = `📁 ${originalName}`;
        titleElement.blur();
      }
    };

    // Store handlers for cleanup
    titleElement._clickHandler = clickHandler;
    titleElement._blurHandler = blurHandler;
    titleElement._keydownHandler = keydownHandler;

    // Attach handlers
    titleElement.addEventListener('click', clickHandler);
    titleElement.addEventListener('blur', blurHandler);
    titleElement.addEventListener('keydown', keydownHandler);
  }

  /**
   * Clean up edit handlers to prevent memory leaks
   */
  cleanupTitleEditHandlers(titleElement) {
    if (titleElement._clickHandler) {
      titleElement.removeEventListener('click', titleElement._clickHandler);
      delete titleElement._clickHandler;
    }
    if (titleElement._blurHandler) {
      titleElement.removeEventListener('blur', titleElement._blurHandler);
      delete titleElement._blurHandler;
    }
    if (titleElement._keydownHandler) {
      titleElement.removeEventListener('keydown', titleElement._keydownHandler);
      delete titleElement._keydownHandler;
    }

    // Clean up dataset
    delete titleElement.dataset.collectionId;
    delete titleElement.dataset.originalName;
  }

  /**
   * Load photos for current view using PhotoSet
   * PhotoSet is the SINGLE SOURCE for all photo data
   */
  async loadPhotos() {
    try {
      // Clear old PhotoSet
      if (this.photoSet) {
        this.photoSet.clear();
        this.photoSet = null;
      }

      // Create PhotoSet for current view
      const definition = this.getSetDefinition();
      this.photoSet = new PhotoSetManager(definition, this.app.api);

      console.log('[CollectionView] Initializing PhotoSet for', definition.type);

      // Load initial window of photos (centered at index 0)
      await this.photoSet.initializeForGrid(0);

      // Update app state from PhotoSet
      this.app.state.photos = this.photoSet.getPhotosInWindow();
      this.app.state.currentPage = 1;
      this.app.state.hasMorePages = this.photoSet.totalCount > this.photoSet.window.windowSize;

      console.log(`[CollectionView] Loaded ${this.app.state.photos.length} of ${this.photoSet.totalCount} photos via PhotoSet`);

      // Render grid
      this.app.components.grid.render();
      this.app.updateLibraryCounts();
      this.app.updateStatusBar();

    } catch (error) {
      console.error('[CollectionView] Failed to load photos:', error);
      this.app.components.toast.show('Failed to load photos', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  /**
   * Get PhotoSet instance for current view
   * Used by Lightbox to access the same PhotoSet
   */
  getPhotoSet() {
    return this.photoSet;
  }

  /**
   * Get PhotoSet definition for current view
   * Used by Lightbox to initialize unbounded navigation
   */
  getSetDefinition() {
    const definition = {
      type: this.viewState.type,
      id: this.viewState.collection?.id || null,
      filters: null,  // Future: add filter support
      sortBy: 'capturedAt',
      sortOrder: 'desc',
      searchQuery: null,
      searchAlpha: 0.5
    };

    // Add search-specific parameters
    if (this.viewState.type === 'search') {
      definition.searchQuery = this.viewState.searchQuery;
      definition.searchAlpha = this.viewState.searchAlpha;
    }

    return definition;
  }
}
