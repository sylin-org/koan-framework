/**
 * Bulk Actions Component
 * Toolbar for batch operations on selected photos
 */

import { getSelectedPhotoIds, formatActionMessage } from '../utils/selection.js';
import { confirmDelete } from '../utils/dialogs.js';
import { executeWithFeedback } from '../utils/operations.js';

export class BulkActions {
  constructor(app) {
    this.app = app;
    this.toolbar = null;
    this.render();
  }

  render() {
    // Create toolbar element
    this.toolbar = document.createElement('div');
    this.toolbar.className = 'bulk-actions-toolbar';
    this.toolbar.innerHTML = `
      <div class="bulk-info">
        <span class="selection-count">0 selected</span>
        <button class="btn-clear-selection">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
          Clear Selection
        </button>
      </div>
      <div class="bulk-buttons">
        <button class="btn-bulk-favorite">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
          </svg>
          Add to Favorites
        </button>
        <button class="btn-bulk-download">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="7 10 12 15 17 10"></polyline>
            <line x1="12" y1="15" x2="12" y2="3"></line>
          </svg>
          Download
        </button>
        <button class="btn-bulk-delete">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="3 6 5 6 21 6"></polyline>
            <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
          </svg>
          Delete
        </button>
      </div>
    `;

    // Append to body (it's fixed position)
    document.body.appendChild(this.toolbar);

    // Setup event listeners
    this.setupListeners();
  }

  setupListeners() {
    // Clear selection
    const btnClear = this.toolbar.querySelector('.btn-clear-selection');
    btnClear.addEventListener('click', () => {
      this.app.clearSelection();
    });

    // Bulk favorite
    const btnFavorite = this.toolbar.querySelector('.btn-bulk-favorite');
    btnFavorite.addEventListener('click', async () => {
      await this.bulkFavorite();
    });

    // Bulk download
    const btnDownload = this.toolbar.querySelector('.btn-bulk-download');
    btnDownload.addEventListener('click', async () => {
      await this.bulkDownload();
    });

    // Bulk delete
    const btnDelete = this.toolbar.querySelector('.btn-bulk-delete');
    btnDelete.addEventListener('click', async () => {
      await this.bulkDelete();
    });
  }

  update(count) {
    const countSpan = this.toolbar.querySelector('.selection-count');
    countSpan.textContent = `${count} selected`;

    if (count > 0) {
      this.show();
    } else {
      this.hide();
    }
  }

  show() {
    this.toolbar.classList.add('visible');
  }

  hide() {
    this.toolbar.classList.remove('visible');
  }

  async bulkFavorite() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/favorite', {
        photoIds: photoIds,
        isFavorite: true
      }),
      {
        successMessage: formatActionMessage(photoIds.length, 'added', { target: 'Favorites' }),
        errorMessage: 'Failed to add photos to favorites',
        successIcon: '⭐',
        reloadPhotos: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }

  async bulkDownload() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

    this.app.components.toast.show(
      formatActionMessage(photoIds.length, 'downloading'),
      { icon: '⬇️', duration: 2000 }
    );

    // Download each photo by opening in new tab (browser will handle download)
    photoIds.forEach(photoId => {
      const url = `/api/photos/${photoId}/download`;
      window.open(url, '_blank');
    });
  }

  async bulkDelete() {
    const photoIds = getSelectedPhotoIds(this.app.state.selectedPhotos, this.app.components.toast);
    if (!photoIds) return;

    if (!confirmDelete(photoIds.length, 'photo')) return;

    await executeWithFeedback(
      () => this.app.api.post('/api/photos/bulk/delete', { photoIds }),
      {
        successMessage: formatActionMessage(photoIds.length, 'deleted'),
        errorMessage: 'Failed to delete photos',
        successIcon: '🗑️',
        reloadPhotos: true,
        clearSelection: true,
        toast: this.app.components.toast,
        app: this.app
      }
    );
  }
}
