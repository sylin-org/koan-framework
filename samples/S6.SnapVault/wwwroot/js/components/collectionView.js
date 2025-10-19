/**
 * CollectionView Component
 * Manages the main content area header and actions for different views
 * Handles context-specific actions: All Photos, Favorites, Collection views
 */

export class CollectionView {
  constructor(app) {
    this.app = app;
    this.currentViewId = 'all-photos'; // 'all-photos' | 'favorites' | collectionId
    this.collection = null;
  }

  /**
   * Load and display a specific view
   */
  async load(viewId) {
    this.currentViewId = viewId;
    this.collection = null;

    // Load collection data if viewing a collection
    if (viewId !== 'all-photos' && viewId !== 'favorites') {
      try {
        this.collection = await this.app.api.get(`/api/collections/${viewId}`);
      } catch (error) {
        console.error('[CollectionView] Failed to load collection:', error);
        this.app.components.toast.show('Failed to load collection', {
          icon: '⚠️',
          duration: 3000
        });
        // Fallback to all photos
        this.currentViewId = 'all-photos';
      }
    }

    // Render header
    this.renderHeader();

    // Load photos for this view
    await this.loadPhotos();

    console.log(`[CollectionView] Loaded view: ${viewId}`);
  }

  /**
   * Render context-specific header with actions
   */
  renderHeader() {
    const header = document.querySelector('.content-header');
    if (!header) {
      console.warn('[CollectionView] Content header not found');
      return;
    }

    // Keep view controls, just update title and actions
    const titleElement = header.querySelector('.page-title');
    if (!titleElement) return;

    // Update title
    if (this.currentViewId === 'all-photos') {
      titleElement.textContent = 'All Photos';
      this.renderAllPhotosActions(header);
    } else if (this.currentViewId === 'favorites') {
      titleElement.textContent = '⭐ Favorites';
      this.renderFavoritesActions(header);
    } else if (this.collection) {
      titleElement.textContent = `📁 ${this.collection.name}`;
      this.renderCollectionActions(header);
    }
  }

  /**
   * Render actions for All Photos view
   */
  renderAllPhotosActions(header) {
    let actionsContainer = header.querySelector('.view-actions');
    if (!actionsContainer) {
      actionsContainer = document.createElement('div');
      actionsContainer.className = 'view-actions';
      header.appendChild(actionsContainer);
    }

    actionsContainer.innerHTML = `
      <button class="btn-delete-selected btn-secondary" title="Permanently delete selected photos">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="3 6 5 6 21 6"></polyline>
          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
        </svg>
        Delete Selected
      </button>
    `;

    this.attachAllPhotosHandlers();
  }

  /**
   * Render actions for Favorites view
   */
  renderFavoritesActions(header) {
    let actionsContainer = header.querySelector('.view-actions');
    if (!actionsContainer) {
      actionsContainer = document.createElement('div');
      actionsContainer.className = 'view-actions';
      header.appendChild(actionsContainer);
    }

    actionsContainer.innerHTML = `
      <button class="btn-unfavorite-selected btn-secondary" title="Remove selected photos from favorites">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
        </svg>
        Remove from Favorites
      </button>
    `;

    this.attachFavoritesHandlers();
  }

  /**
   * Render actions for Collection view
   */
  renderCollectionActions(header) {
    let actionsContainer = header.querySelector('.view-actions');
    if (!actionsContainer) {
      actionsContainer = document.createElement('div');
      actionsContainer.className = 'view-actions';
      header.appendChild(actionsContainer);
    }

    const percentage = (this.collection.photoCount / 2048) * 100;
    const isNearLimit = percentage > 75;

    actionsContainer.innerHTML = `
      <div class="collection-capacity ${isNearLimit ? 'warning' : ''}">
        <div class="capacity-bar">
          <div class="capacity-fill ${percentage > 90 ? 'warning' : ''}" style="width: ${percentage}%"></div>
        </div>
        <span class="capacity-text">${this.collection.photoCount} / 2,048</span>
      </div>
      <button class="btn-remove-from-collection btn-secondary" title="Remove selected photos from this collection">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="18" y1="6" x2="6" y2="18"></line>
          <line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
        Remove from Collection
      </button>
    `;

    this.attachCollectionHandlers();
  }

  /**
   * Attach event handlers for All Photos view actions
   */
  attachAllPhotosHandlers() {
    const btnDelete = document.querySelector('.btn-delete-selected');
    if (btnDelete) {
      btnDelete.addEventListener('click', async () => {
        await this.handleDeleteSelected();
      });
    }

    // Keyboard shortcut: Delete key
    this.attachKeyboardHandler('Delete', () => this.handleDeleteSelected());
  }

  /**
   * Attach event handlers for Favorites view actions
   */
  attachFavoritesHandlers() {
    const btnUnfavorite = document.querySelector('.btn-unfavorite-selected');
    if (btnUnfavorite) {
      btnUnfavorite.addEventListener('click', async () => {
        await this.handleUnfavoriteSelected();
      });
    }

    // Keyboard shortcut: Delete key (unfavorite in Favorites view)
    this.attachKeyboardHandler('Delete', () => this.handleUnfavoriteSelected());
  }

  /**
   * Attach event handlers for Collection view actions
   */
  attachCollectionHandlers() {
    const btnRemove = document.querySelector('.btn-remove-from-collection');
    if (btnRemove) {
      btnRemove.addEventListener('click', async () => {
        await this.handleRemoveFromCollection();
      });
    }

    // Keyboard shortcut: Delete key (remove from collection)
    this.attachKeyboardHandler('Delete', () => this.handleRemoveFromCollection());
  }

  /**
   * Attach keyboard handler (removes previous handler first)
   */
  attachKeyboardHandler(key, handler) {
    // Remove existing handler
    if (this.keyboardHandler) {
      document.removeEventListener('keydown', this.keyboardHandler);
    }

    // Attach new handler
    this.keyboardHandler = (e) => {
      if (e.key === key && !e.target.closest('input, textarea, [contenteditable="true"]')) {
        e.preventDefault();
        handler();
      }
    };

    document.addEventListener('keydown', this.keyboardHandler);
  }

  /**
   * Handle permanent delete (All Photos view only)
   */
  async handleDeleteSelected() {
    const selectedIds = Array.from(this.app.state.selectedPhotos);
    if (selectedIds.length === 0) {
      this.app.components.toast.show('No photos selected', { icon: 'ℹ️', duration: 2000 });
      return;
    }

    const confirmed = confirm(
      `Permanently delete ${selectedIds.length} photo${selectedIds.length > 1 ? 's' : ''}?\n\n` +
      `This will delete the photo${selectedIds.length > 1 ? 's' : ''} and all thumbnails from storage.\n` +
      `This cannot be undone.`
    );

    if (!confirmed) return;

    try {
      // Use bulk delete endpoint
      const result = await this.app.api.post('/api/photos/bulk/delete', {
        photoIds: selectedIds
      });

      this.app.components.toast.show(
        `Deleted ${result.deleted} photo${result.deleted !== 1 ? 's' : ''}`,
        { icon: '🗑️', duration: 3000 }
      );

      // Reload photos
      await this.loadPhotos();

      // Clear selection
      this.app.clearSelection();
    } catch (error) {
      console.error('[CollectionView] Failed to delete photos:', error);
      this.app.components.toast.show('Failed to delete photos', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  /**
   * Handle unfavorite (Favorites view)
   */
  async handleUnfavoriteSelected() {
    const selectedIds = Array.from(this.app.state.selectedPhotos);
    if (selectedIds.length === 0) {
      this.app.components.toast.show('No photos selected', { icon: 'ℹ️', duration: 2000 });
      return;
    }

    try {
      // Use bulk favorite endpoint with isFavorite: false
      await this.app.api.post('/api/photos/bulk/favorite', {
        photoIds: selectedIds,
        isFavorite: false
      });

      this.app.components.toast.show(
        `Removed ${selectedIds.length} photo${selectedIds.length !== 1 ? 's' : ''} from Favorites`,
        { icon: '⭐', duration: 2000 }
      );

      // Reload photos
      await this.loadPhotos();

      // Clear selection
      this.app.clearSelection();
    } catch (error) {
      console.error('[CollectionView] Failed to unfavorite photos:', error);
      this.app.components.toast.show('Failed to update favorites', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  /**
   * Handle remove from collection (Collection view)
   */
  async handleRemoveFromCollection() {
    const selectedIds = Array.from(this.app.state.selectedPhotos);
    if (selectedIds.length === 0) {
      this.app.components.toast.show('No photos selected', { icon: 'ℹ️', duration: 2000 });
      return;
    }

    try {
      await this.app.api.post(`/api/collections/${this.currentViewId}/photos/remove`, {
        photoIds: selectedIds
      });

      this.app.components.toast.show(
        `Removed ${selectedIds.length} photo${selectedIds.length !== 1 ? 's' : ''} from collection`,
        { icon: '✓', duration: 2000 }
      );

      // Reload photos
      await this.loadPhotos();

      // Reload collections sidebar to update counts
      if (this.app.components.collectionsSidebar) {
        await this.app.components.collectionsSidebar.loadCollections();
        this.app.components.collectionsSidebar.render();
      }

      // Clear selection
      this.app.clearSelection();
    } catch (error) {
      console.error('[CollectionView] Failed to remove photos from collection:', error);
      this.app.components.toast.show('Failed to remove photos', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  /**
   * Load photos for current view
   */
  async loadPhotos() {
    try {
      if (this.currentViewId === 'all-photos') {
        await this.app.loadPhotos();
      } else if (this.currentViewId === 'favorites') {
        await this.app.filterPhotos('favorites');
      } else if (this.collection) {
        // Load collection photos
        const response = await this.app.api.get(`/api/collections/${this.currentViewId}/photos`);
        this.app.state.photos = response.photos || [];
        this.app.state.currentPage = 1;
        this.app.state.hasMorePages = false; // Collections don't support infinite scroll yet
        this.app.components.grid.render();
        this.app.updateLibraryCounts();
        this.app.updateStatusBar();
      }
    } catch (error) {
      console.error('[CollectionView] Failed to load photos:', error);
      this.app.components.toast.show('Failed to load photos', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }
}
