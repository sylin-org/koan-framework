/**
 * Context Panel Component
 * Right sidebar that transforms based on application state:
 * - Collection Properties: When viewing a collection (no selection)
 * - Selection Actions: When photos are selected
 *
 * REFACTORED: Now uses centralized Button and Icon components
 */

import { getSelectedPhotoIds, formatActionMessage } from '../utils/selection.js';
import { confirmDelete } from '../utils/dialogs.js';
import { executeWithFeedback } from '../utils/operations.js';
import { pluralize } from '../utils/html.js';
import { Button } from '../system/Button.js';
import { Icon } from '../system/Icon.js';

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
    if (viewState.type === 'collection') {
      // Collection view: show collection properties + selection actions (if any)
      this.renderCollectionView(viewState.collection, selectionCount);
    } else if (selectionCount > 0) {
      // All Photos/Favorites with selection: show only selection actions
      this.renderSelectionActions(selectionCount, true); // true = allow delete
    } else {
      // All Photos or Favorites - hide panel
      this.container.innerHTML = '<div class="context-panel-empty"></div>';
    }
  }

  /**
   * Render Collection View (properties + optional selection actions)
   * Shows collection properties at top, selection actions below when photos selected
   * REFACTORED: Uses Button component system
   */
  renderCollectionView(collection, selectionCount) {
    const photoCount = collection.photoCount || 0;

    // Create panel content container
    const panelContent = document.createElement('div');
    panelContent.className = 'panel-content';

    // Details Section (still HTML for simplicity, could be componentized later)
    const detailsSection = document.createElement('section');
    detailsSection.className = 'panel-section';
    detailsSection.innerHTML = `
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
    `;
    panelContent.appendChild(detailsSection);

    // Actions Section - REFACTORED with Button component
    const actionsSection = document.createElement('section');
    actionsSection.className = 'panel-section';

    const actionsTitle = document.createElement('h3');
    actionsTitle.textContent = 'Actions';
    actionsSection.appendChild(actionsTitle);

    // Create collection action buttons using Button component
    const collectionActions = Button.createGroup([
      {
        label: 'Rename Collection',
        icon: 'edit',
        dataAction: 'rename',
        onClick: () => this.triggerHeaderTitleEdit()
      },
      {
        label: 'Duplicate Collection',
        icon: 'copy',
        dataAction: 'duplicate',
        onClick: () => this.handleDuplicateCollection(collection)
      },
      {
        label: 'Export Collection...',
        icon: 'download',
        dataAction: 'export',
        onClick: () => this.handleExportCollection(collection)
      },
      {
        label: 'Delete Collection',
        icon: 'trash',
        variant: 'destructive',
        dataAction: 'delete',
        onClick: () => this.handleDeleteCollection(collection)
      }
    ]);

    actionsSection.appendChild(collectionActions);
    panelContent.appendChild(actionsSection);

    // Add selection actions if photos are selected
    if (selectionCount > 0) {
      const selectionSection = this.createSelectionActionsSection(selectionCount, false);
      panelContent.appendChild(selectionSection);
    }

    // Replace container content
    this.container.innerHTML = '';
    this.container.appendChild(panelContent);
  }

  /**
   * Render Selection Actions state (for All Photos/Favorites views)
   * REFACTORED: Uses Button component system
   */
  renderSelectionActions(count, allowDelete = true) {
    const panelContent = document.createElement('div');
    panelContent.className = 'panel-content';

    const selectionSection = this.createSelectionActionsSection(count, allowDelete);
    panelContent.appendChild(selectionSection);

    this.container.innerHTML = '';
    this.container.appendChild(panelContent);
  }

  /**
   * Create Selection Actions Section (DOM element)
   * REFACTORED: Uses Button component system
   * @param {number} count - Number of selected photos
   * @param {boolean} allowDelete - Whether to show Delete button (false in collections)
   * @returns {HTMLElement} Section element with photo actions
   */
  createSelectionActionsSection(count, allowDelete = true) {
    const { viewState } = this.app.components.collectionView;
    const isInCollection = viewState.type === 'collection';
    const isInFavorites = viewState.type === 'favorites';

    const section = document.createElement('section');
    section.className = 'panel-section';

    const title = document.createElement('h3');
    title.textContent = `${count} ${pluralize(count, 'Photo')} Selected`;
    section.appendChild(title);

    // Build actions array based on context
    const actions = [];

    // Add to Favorites (always show)
    actions.push({
      label: 'Add to Favorites',
      icon: 'star',
      dataAction: 'add-favorites',
      onClick: () => this.handleAddToFavorites()
    });

    // Remove from Favorites (only in favorites view)
    if (isInFavorites) {
      actions.push({
        label: 'Remove from Favorites',
        icon: 'star',
        dataAction: 'remove-favorites',
        onClick: () => this.handleRemoveFromFavorites()
      });
    }

    // Add to Collection (not in collection view)
    if (!isInCollection) {
      actions.push({
        label: 'Add to Collection...',
        icon: 'folderPlus',
        dataAction: 'add-to-collection',
        onClick: () => this.handleAddToCollection()
      });
    }

    // Remove from Collection (only in collection view)
    if (isInCollection) {
      actions.push({
        label: 'Remove from Collection',
        icon: 'x',
        dataAction: 'remove-from-collection',
        onClick: () => this.handleRemoveFromCollection()
      });
    }

    // Download (always show)
    actions.push({
      label: `Download (${count})`,
      icon: 'download',
      dataAction: 'download',
      onClick: () => this.handleDownload()
    });

    // Analyze with AI (always show)
    actions.push({
      label: 'Analyze with AI',
      icon: 'sparkles',
      dataAction: 'analyze-ai',
      onClick: () => this.handleAnalyzeAI()
    });

    // Delete (conditional)
    if (allowDelete) {
      actions.push({
        label: `Delete (${count})`,
        icon: 'trash',
        variant: 'destructive',
        dataAction: 'delete-photos',
        onClick: () => this.handleDeletePhotos()
      });
    }

    // Create button group using Button component
    const actionsGrid = Button.createGroup(actions);
    section.appendChild(actionsGrid);

    return section;
  }

  // NOTE: attachCollectionHandlers() removed - now using onClick in Button component

  /**
   * Trigger edit mode on the main header title
   */
  triggerHeaderTitleEdit() {
    const titleElement = document.querySelector('.content-header .page-title');
    if (titleElement && titleElement.classList.contains('editable')) {
      titleElement.click();
    }
  }

  // NOTE: attachSelectionHandlers() removed - now using onClick in Button component

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

  async handleAddToFavorites() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

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

  async handleRemoveFromFavorites() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

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

  async handleAddToCollection() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

    // TODO: Show collection picker dialog
    this.app.components.toast.show('Collection picker coming soon', {
      icon: 'ℹ️',
      duration: 2000
    });
  }

  async handleRemoveFromCollection() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

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

  handleDownload() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

    this.app.components.toast.show(
      formatActionMessage(photoIds.length, 'downloading'),
      { icon: '⬇️', duration: 2000 }
    );

    photoIds.forEach(photoId => {
      const url = `/api/photos/${photoId}/download`;
      window.open(url, '_blank');
    });
  }

  async handleAnalyzeAI() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

    // TODO: Implement AI analysis
    this.app.components.toast.show('AI analysis coming soon', {
      icon: '🤖',
      duration: 2000
    });
  }

  async handleDeletePhotos() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;
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
