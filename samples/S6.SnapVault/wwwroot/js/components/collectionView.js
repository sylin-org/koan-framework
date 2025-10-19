/**
 * CollectionView Component
 * Manages the main content area header and actions for different views
 * Handles context-specific actions: All Photos, Favorites, Collection views
 *
 * State Management:
 * Single viewState object determines all UI rendering and behavior
 * - { type: 'all-photos' }
 * - { type: 'favorites' }
 * - { type: 'collection', collection: {...} }
 */

import { getSelectedPhotoIds, formatActionMessage } from '../utils/selection.js';
import { confirmDelete } from '../utils/dialogs.js';
import { executeWithFeedback } from '../utils/operations.js';
import { pluralize } from '../utils/html.js';

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
   * Render context-specific header with actions
   */
  renderHeader() {
    const header = document.querySelector('.content-header');
    if (!header) {
      console.warn('[CollectionView] Content header not found');
      return;
    }

    const titleElement = header.querySelector('.page-title');
    if (!titleElement) return;

    // Clear any previous edit handlers
    this.cleanupTitleEditHandlers(titleElement);

    // Render based on view type
    switch (this.viewState.type) {
      case 'all-photos':
        titleElement.textContent = 'All Photos';
        titleElement.contentEditable = false;
        titleElement.classList.remove('editable');
        this.renderAllPhotosActions(header);
        break;

      case 'favorites':
        titleElement.textContent = '⭐ Favorites';
        titleElement.contentEditable = false;
        titleElement.classList.remove('editable');
        this.renderFavoritesActions(header);
        break;

      case 'collection':
        const { collection } = this.viewState;
        titleElement.textContent = `📁 ${collection.name}`;
        titleElement.contentEditable = false;
        titleElement.classList.add('editable');
        titleElement.dataset.collectionId = collection.id;
        titleElement.dataset.originalName = collection.name;
        this.attachTitleEditHandlers(titleElement);
        this.renderCollectionActions(header);
        break;

      default:
        console.warn('[CollectionView] Unknown view type:', this.viewState.type);
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

    const { collection } = this.viewState;
    const percentage = (collection.photoCount / 2048) * 100;
    const isNearLimit = percentage > 75;

    actionsContainer.innerHTML = `
      <div class="collection-capacity ${isNearLimit ? 'warning' : ''}">
        <div class="capacity-bar">
          <div class="capacity-fill ${percentage > 90 ? 'warning' : ''}" style="width: ${percentage}%"></div>
        </div>
        <span class="capacity-text">${collection.photoCount} / 2,048</span>
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
    const selectedIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!selectedIds) return;

    const additionalInfo = `This will delete the ${pluralize(selectedIds.length, 'photo')} and all thumbnails from storage.`;
    if (!confirmDelete(selectedIds.length, 'photo', { additionalInfo })) return;

    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/delete', { photoIds: selectedIds }),
      {
        successMessage: formatActionMessage(selectedIds.length, 'deleted'),
        errorMessage: 'Failed to delete photos',
        successIcon: '🗑️',
        reloadCurrentView: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  /**
   * Handle unfavorite (Favorites view)
   */
  async handleUnfavoriteSelected() {
    const selectedIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!selectedIds) return;

    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/favorite', {
        photoIds: selectedIds,
        isFavorite: false
      }),
      {
        successMessage: formatActionMessage(selectedIds.length, 'removed', { from: 'Favorites' }),
        errorMessage: 'Failed to update favorites',
        successIcon: '⭐',
        reloadCurrentView: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  /**
   * Handle remove from collection (Collection view)
   */
  async handleRemoveFromCollection() {
    const selectedIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!selectedIds) return;

    if (this.viewState.type !== 'collection') {
      console.error('[CollectionView] Cannot remove from collection - not in collection view');
      return;
    }

    await executeWithFeedback(
      () => this.app.api.post(`/api/collections/${this.viewState.collection.id}/photos/remove`, {
        photoIds: selectedIds
      }),
      {
        successMessage: formatActionMessage(selectedIds.length, 'removed', { from: 'collection' }),
        errorMessage: 'Failed to remove photos',
        successIcon: '✓',
        reloadCurrentView: true,
        reloadCollections: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  /**
   * Load photos for current view
   * Delegates to appropriate loading strategy based on view type
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
