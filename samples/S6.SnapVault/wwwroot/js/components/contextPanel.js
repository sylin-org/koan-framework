/**
 * Context Panel Component
 * Right sidebar that transforms based on application state:
 * - Collection Properties: When viewing a collection (no selection)
 * - Selection Actions: When photos are selected
 */

import { getSelectedPhotoIds, formatActionMessage } from '../utils/selection.js';
import { confirmDelete } from '../utils/dialogs.js';
import { executeWithFeedback } from '../utils/operations.js';
import { pluralize } from '../utils/html.js';

export class ContextPanel {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.sidebar-right .filters-panel');
    this.currentState = null;
  }

  /**
   * Update panel based on current application state
   */
  update() {
    if (!this.container) return;

    const { viewState } = this.app.components.collectionView;
    const selectionCount = this.app.state.selectedPhotos.size;

    // Determine which state to show
    if (selectionCount > 0) {
      this.renderSelectionActions(selectionCount);
    } else if (viewState.type === 'collection') {
      this.renderCollectionProperties(viewState.collection);
    } else {
      // All Photos or Favorites - hide panel
      this.container.innerHTML = '<div class="context-panel-empty"></div>';
    }
  }

  /**
   * Render Collection Properties state
   * Uses exact same structure as Photo Information lightbox panel
   */
  renderCollectionProperties(collection) {
    const percentage = (collection.photoCount / 2048) * 100;
    const photoCount = collection.photoCount || 0;

    this.container.innerHTML = `
      <div class="panel-content">
        <!-- Details Section -->
        <section class="panel-section">
          <h3>Details</h3>
          <div class="metadata-grid">
            <div class="metadata-item">
              <span class="label">Name</span>
              <span class="value">${this.escapeHtml(collection.name)}</span>
            </div>
            <div class="metadata-item">
              <span class="label">Capacity</span>
              <span class="value">${photoCount} / 2,048 photos</span>
            </div>
            <div class="metadata-item">
              <span class="label">Type</span>
              <span class="value">Manual Collection</span>
            </div>
            <div class="metadata-item">
              <span class="label">Created</span>
              <span class="value">${this.formatDate(collection.createdAt)}</span>
            </div>
          </div>
        </section>

        <!-- Actions Section -->
        <section class="panel-section">
          <h3>Actions</h3>
          <div class="actions-grid">
            <button class="btn-action" data-action="rename">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path>
                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path>
              </svg>
              <span class="action-label">Rename Collection</span>
            </button>

            <button class="btn-action" data-action="duplicate">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
              </svg>
              <span class="action-label">Duplicate Collection</span>
            </button>

            <button class="btn-action" data-action="export">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
              <span class="action-label">Export Collection...</span>
            </button>

            <button class="btn-action btn-destructive" data-action="delete">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
              <span class="action-label">Delete Collection</span>
            </button>
          </div>
        </section>
      </div>
    `;

    this.attachCollectionHandlers(collection);
  }

  /**
   * Render Selection Actions state
   * Uses exact same structure as Photo Information lightbox panel
   */
  renderSelectionActions(count) {
    const { viewState } = this.app.components.collectionView;
    const isInCollection = viewState.type === 'collection';
    const isInFavorites = viewState.type === 'favorites';

    this.container.innerHTML = `
      <div class="panel-content">
        <!-- Selection Info Section -->
        <section class="panel-section">
          <h3>${count} ${pluralize(count, 'Photo')} Selected</h3>
          <div class="actions-grid">
            <button class="btn-action" data-action="add-favorites">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
              </svg>
              <span class="action-label">Add to Favorites</span>
            </button>

            ${isInFavorites ? `
            <button class="btn-action" data-action="remove-favorites">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
              </svg>
              <span class="action-label">Remove from Favorites</span>
            </button>
            ` : ''}

            <button class="btn-action" data-action="add-to-collection">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                <line x1="12" y1="11" x2="12" y2="17"></line>
                <line x1="9" y1="14" x2="15" y2="14"></line>
              </svg>
              <span class="action-label">Add to Collection...</span>
            </button>

            ${isInCollection ? `
            <button class="btn-action" data-action="remove-from-collection">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
              <span class="action-label">Remove from Collection</span>
            </button>
            ` : ''}

            <button class="btn-action" data-action="download">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                <polyline points="7 10 12 15 17 10"></polyline>
                <line x1="12" y1="15" x2="12" y2="3"></line>
              </svg>
              <span class="action-label">Download (${count})</span>
            </button>

            <button class="btn-action" data-action="analyze-ai">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"></circle>
                <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"></path>
                <line x1="12" y1="17" x2="12.01" y2="17"></line>
              </svg>
              <span class="action-label">Analyze with AI</span>
            </button>

            <button class="btn-action btn-destructive" data-action="delete-photos">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
              <span class="action-label">Delete (${count})</span>
            </button>
          </div>
        </section>
      </div>
    `;

    this.attachSelectionHandlers();
  }

  /**
   * Attach event handlers for collection actions
   */
  attachCollectionHandlers(collection) {
    // Action buttons
    this.container.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const action = btn.dataset.action;
        switch (action) {
          case 'rename':
            this.triggerHeaderTitleEdit();
            break;
          case 'duplicate':
            await this.handleDuplicateCollection(collection);
            break;
          case 'export':
            await this.handleExportCollection(collection);
            break;
          case 'delete':
            await this.handleDeleteCollection(collection);
            break;
        }
      });
    });
  }

  /**
   * Trigger edit mode on the main header title
   */
  triggerHeaderTitleEdit() {
    const titleElement = document.querySelector('.content-header .page-title');
    if (titleElement && titleElement.classList.contains('editable')) {
      titleElement.click();
    }
  }

  /**
   * Attach event handlers for selection actions
   */
  attachSelectionHandlers() {
    this.container.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const action = btn.dataset.action;
        const selectedIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
        if (!selectedIds) return;

        switch (action) {
          case 'add-favorites':
            await this.handleAddToFavorites(selectedIds);
            break;
          case 'remove-favorites':
            await this.handleRemoveFromFavorites(selectedIds);
            break;
          case 'add-to-collection':
            await this.handleAddToCollection(selectedIds);
            break;
          case 'remove-from-collection':
            await this.handleRemoveFromCollection(selectedIds);
            break;
          case 'download':
            this.handleDownload(selectedIds);
            break;
          case 'analyze-ai':
            await this.handleAnalyzeAI(selectedIds);
            break;
          case 'delete-photos':
            await this.handleDeletePhotos(selectedIds);
            break;
        }
      });
    });
  }

  // ==================== Collection Actions ====================

  async handleDuplicateCollection(collection) {
    // TODO: Implement duplicate functionality
    this.app.components.toast.show('Duplicate collection coming soon', {
      icon: 'ℹ️',
      duration: 2000
    });
  }

  async handleExportCollection(collection) {
    // TODO: Implement export functionality
    this.app.components.toast.show('Export collection coming soon', {
      icon: 'ℹ️',
      duration: 2000
    });
  }

  async handleDeleteCollection(collection) {
    if (this.app.components.collectionsSidebar) {
      await this.app.components.collectionsSidebar.deleteCollection(collection.id);
    }
  }

  // ==================== Selection Actions ====================

  async handleAddToFavorites(photoIds) {
    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/favorite', {
        photoIds: photoIds,
        isFavorite: true
      }),
      {
        successMessage: formatActionMessage(photoIds.length, 'added', { target: 'Favorites' }),
        errorMessage: 'Failed to add to favorites',
        successIcon: '⭐',
        reloadCurrentView: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  async handleRemoveFromFavorites(photoIds) {
    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/favorite', {
        photoIds: photoIds,
        isFavorite: false
      }),
      {
        successMessage: formatActionMessage(photoIds.length, 'removed', { from: 'Favorites' }),
        errorMessage: 'Failed to update favorites',
        successIcon: '⭐',
        reloadCurrentView: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  async handleAddToCollection(photoIds) {
    // TODO: Show collection picker dialog
    this.app.components.toast.show('Collection picker coming soon', {
      icon: 'ℹ️',
      duration: 2000
    });
  }

  async handleRemoveFromCollection(photoIds) {
    const { viewState } = this.app.components.collectionView;
    if (viewState.type !== 'collection') return;

    await executeWithFeedback(
      () => this.app.api.post(`/api/collections/${viewState.collection.id}/photos/remove`, {
        photoIds: photoIds
      }),
      {
        successMessage: formatActionMessage(photoIds.length, 'removed', { from: 'collection' }),
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

  handleDownload(photoIds) {
    this.app.components.toast.show(
      formatActionMessage(photoIds.length, 'downloading'),
      { icon: '⬇️', duration: 2000 }
    );

    photoIds.forEach(photoId => {
      const url = `/api/photos/${photoId}/download`;
      window.open(url, '_blank');
    });
  }

  async handleAnalyzeAI(photoIds) {
    // TODO: Implement AI analysis
    this.app.components.toast.show('AI analysis coming soon', {
      icon: '🤖',
      duration: 2000
    });
  }

  async handleDeletePhotos(photoIds) {
    const additionalInfo = `This will delete the ${pluralize(photoIds.length, 'photo')} and all thumbnails from storage.`;
    if (!confirmDelete(photoIds.length, 'photo', { additionalInfo })) return;

    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/delete', { photoIds }),
      {
        successMessage: formatActionMessage(photoIds.length, 'deleted'),
        errorMessage: 'Failed to delete photos',
        successIcon: '🗑️',
        reloadCurrentView: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  // ==================== Utilities ====================

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  formatDate(dateString) {
    const date = new Date(dateString);
    return date.toLocaleString('en-US', {
      month: 'long',
      day: 'numeric',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit'
    });
  }
}
