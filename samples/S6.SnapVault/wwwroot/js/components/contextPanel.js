/**
 * Context Panel Component
 * Right sidebar that transforms based on application state:
 * - Collection Properties: When viewing a collection (no selection)
 * - Selection Actions: When photos are selected
 *
 * REFACTORED Phase 1: Uses centralized Button and Icon components
 * REFACTORED Phase 2: Uses ActionExecutor for all operations
 */

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
    if (!this.app.actions) {
      console.error('[ContextPanel] app.actions is not initialized!');
      return;
    }

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

    // Actions Section - REFACTORED Phase 2: Uses ActionExecutor
    const actionsSection = document.createElement('section');
    actionsSection.className = 'panel-section';

    const actionsTitle = document.createElement('h3');
    actionsTitle.textContent = 'Actions';
    actionsSection.appendChild(actionsTitle);

    // Create collection action buttons using ActionExecutor
    const collectionActions = this.app.actions.createButtonGroup([
      'collection.rename',
      'collection.duplicate',
      'collection.export',
      'collection.delete'
    ], collection);

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
    if (!this.app.actions) {
      console.error('[ContextPanel] app.actions is not initialized!');
      return;
    }

    const panelContent = document.createElement('div');
    panelContent.className = 'panel-content';

    const selectionSection = this.createSelectionActionsSection(count, allowDelete);
    panelContent.appendChild(selectionSection);

    this.container.innerHTML = '';
    this.container.appendChild(panelContent);
  }

  /**
   * Create Selection Actions Section (DOM element)
   * REFACTORED Phase 2: Uses ActionExecutor
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
    const actionIds = [];

    // Add to Favorites (always show)
    actionIds.push('photo.favorite');

    // Remove from Favorites (only in favorites view)
    if (isInFavorites) {
      actionIds.push('photo.unfavorite');
    }

    // Add to Collection (not in collection view)
    if (!isInCollection) {
      actionIds.push('photo.addToCollection');
    }

    // Remove from Collection (only in collection view)
    if (isInCollection) {
      actionIds.push('photo.removeFromCollection');
    }

    // Download (always show) - with custom label showing count
    actionIds.push({
      id: 'photo.download',
      options: {
        label: this.app.actions.getLabelWithCount('photo.download', count)
      }
    });

    // Analyze with AI (always show)
    actionIds.push('photo.analyzeAI');

    // Delete (conditional)
    if (allowDelete) {
      actionIds.push({
        id: 'photo.delete',
        options: {
          label: this.app.actions.getLabelWithCount('photo.delete', count)
        }
      });
    }

    // Create button group using ActionExecutor (context=null means use selection)
    const actionsGrid = this.app.actions.createButtonGroup(actionIds, null);
    section.appendChild(actionsGrid);

    return section;
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
