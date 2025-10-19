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
 */

export class CollectionView {
  constructor(app) {
    this.app = app;
    this.viewState = { type: 'all-photos' }; // Single source of truth
  }

  /**
   * Set view and load data
   * Single entry point for all view transitions
   */
  async setView(viewId) {
    console.log(`[CollectionView] Setting view to: ${viewId}`);

    // Build new state object based on viewId
    if (viewId === 'all-photos') {
      this.viewState = { type: 'all-photos' };
    } else if (viewId === 'favorites') {
      this.viewState = { type: 'favorites' };
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
   * Load photos for current view
   * Delegates to appropriate loading strategy based on view type
   * NOTE: Actions are now handled by ContextPanel component
   */
  async loadPhotos() {
    try {
      switch (this.viewState.type) {
        case 'all-photos':
          await this.app.loadPhotos();
          break;

        case 'favorites':
          await this.app.filterPhotos('favorites');
          break;

        case 'collection':
          await this.loadCollectionPhotos();
          break;

        default:
          console.warn('[CollectionView] Unknown view type:', this.viewState.type);
      }
    } catch (error) {
      console.error('[CollectionView] Failed to load photos:', error);
      this.app.components.toast.show('Failed to load photos', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  /**
   * Load photos for a specific collection
   */
  async loadCollectionPhotos() {
    // STEP 0: Refresh collection data from server to get current photoIds
    // This ensures we have the latest data after operations like remove/add
    try {
      const freshCollection = await this.app.api.get(`/api/collections/${this.viewState.collection.id}`);
      this.viewState.collection = freshCollection;
      console.log('[CollectionView] Refreshed collection data:', freshCollection.photoIds?.length || 0, 'photos');
    } catch (error) {
      console.error('[CollectionView] Failed to refresh collection data:', error);
      this.app.components.toast.show('Failed to refresh collection', {
        icon: '⚠️',
        duration: 3000
      });
      return;
    }

    const { collection } = this.viewState;

    // STEP 1: Flush existing state completely
    this.app.state.photos = [];
    this.app.state.currentPage = 1;
    this.app.state.hasMorePages = false;

    // STEP 2: Clear DOM immediately (don't wait for render)
    const photoGrid = document.querySelector('.photo-grid');
    if (photoGrid) {
      const existingCards = photoGrid.querySelectorAll('.photo-card');
      existingCards.forEach(card => card.remove());
    }

    // STEP 3: Load photos for this collection using available photoIds
    if (collection.photoIds && collection.photoIds.length > 0) {
      console.log('[CollectionView] Loading', collection.photoIds.length, 'photos for collection');

      // Fetch each photo individually using the IDs we have
      const photoPromises = collection.photoIds.map(id =>
        this.app.api.get(`/api/photos/${id}`)
          .catch(err => {
            console.warn('[CollectionView] Failed to load photo', id, err);
            return null;
          })
      );

      const photos = await Promise.all(photoPromises);
      this.app.state.photos = photos.filter(p => p != null);

      console.log('[CollectionView] Successfully loaded', this.app.state.photos.length, 'of', collection.photoIds.length, 'photos');
    } else {
      console.log('[CollectionView] Collection has no photos');
    }

    // STEP 4: Rebuild grid from scratch
    this.app.components.grid.render();
    this.app.updateLibraryCounts();
    this.app.updateStatusBar();
  }
}
