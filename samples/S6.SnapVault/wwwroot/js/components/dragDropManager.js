/**
 * DragDropManager Component
 * Manages drop zones for collections and "New Collection" button
 * Handles photo drag-and-drop to organize into collections
 */

export class DragDropManager {
  constructor(app) {
    this.app = app;
    this.dragOverTarget = null;
  }

  init() {
    this.attachDropZoneHandlers();
    console.log('[DragDropManager] Initialized drop zone handlers');
  }

  attachDropZoneHandlers() {
    const sidebar = document.querySelector('.sidebar-left');
    if (!sidebar) {
      console.warn('[DragDropManager] Sidebar not found');
      return;
    }

    console.log('[DragDropManager] Attaching drop zone handlers to sidebar');

    // Dragover on sidebar - show drop zones and highlight targets
    sidebar.addEventListener('dragover', (e) => {
      e.preventDefault();
      e.dataTransfer.dropEffect = 'copy';

      // Find the target (collection item or new collection button)
      const collectionItem = e.target.closest('.collection-item[data-droppable="true"]');
      const newCollectionBtn = e.target.closest('.btn-new-collection');

      if (collectionItem || newCollectionBtn) {
        console.log('[DragDropManager] Dragover target:', collectionItem ? 'collection' : 'new-collection-btn');
      }

      // Clear previous highlights
      if (this.dragOverTarget && this.dragOverTarget !== collectionItem && this.dragOverTarget !== newCollectionBtn) {
        this.clearDropTargetHighlight();
      }

      // Highlight current target
      if (collectionItem) {
        this.dragOverTarget = collectionItem;
        collectionItem.classList.add('drop-target');
      } else if (newCollectionBtn) {
        this.dragOverTarget = newCollectionBtn;
        newCollectionBtn.classList.add('drop-target');
      } else {
        this.dragOverTarget = null;
      }
    });

    // Dragleave - clear highlights when leaving sidebar
    sidebar.addEventListener('dragleave', (e) => {
      // Only clear if leaving the sidebar entirely
      if (!sidebar.contains(e.relatedTarget)) {
        this.clearDropTargetHighlight();
        this.dragOverTarget = null;
      }
    });

    // Drop handler - add photos to collection or create new collection
    sidebar.addEventListener('drop', async (e) => {
      console.log('[DragDropManager] Drop event fired!', e.target);
      e.preventDefault();

      // Clear visual feedback
      this.clearDropTargetHighlight();

      // Get selected photo IDs from current text selection
      const photoIds = this.app.components.photoSelection.getSelectedPhotoIds();
      console.log('[DragDropManager] Got photoIds:', photoIds);

      if (photoIds.length === 0) {
        console.warn('[DragDropManager] No photos selected');
        return;
      }

      // Determine drop target
      const collectionItem = e.target.closest('.collection-item[data-droppable="true"]');
      const newCollectionBtn = e.target.closest('.btn-new-collection');
      console.log('[DragDropManager] Drop targets - collection:', collectionItem, 'newBtn:', newCollectionBtn);

      if (newCollectionBtn) {
        // Create new collection with these photos
        await this.createCollectionWithPhotos(photoIds);
      } else if (collectionItem) {
        // Add to existing collection
        const collectionId = collectionItem.dataset.collectionId;
        await this.addPhotosToCollection(collectionId, photoIds);
      }

      this.dragOverTarget = null;
    });
  }

  /**
   * Create new collection and add photos to it
   */
  async createCollectionWithPhotos(photoIds) {
    console.log('[DragDropManager] Creating collection with photos:', photoIds);
    const collectionName = prompt('New collection name:');
    if (!collectionName || collectionName.trim() === '') {
      console.log('[DragDropManager] Collection creation cancelled');
      return;
    }

    try {
      // Create collection
      const collection = await this.app.api.post('/api/collections', {
        name: collectionName.trim()
      });

      // Add photos to collection
      const addResult = await this.app.api.post(`/api/collections/${collection.id}/photos`, {
        photoIds: photoIds
      });

      // Reload collections sidebar
      if (this.app.components.collectionsSidebar) {
        await this.app.components.collectionsSidebar.loadCollections();
        this.app.components.collectionsSidebar.render();
      }

      this.app.components.toast.show(
        `Created "${collectionName}" with ${addResult.added} photo${addResult.added !== 1 ? 's' : ''}`,
        { icon: '📁', duration: 3000 }
      );

      // Clear text selection after successful drop
      this.app.components.photoSelection.clearSelection();

      console.log(`[DragDropManager] Created collection "${collectionName}" with ${addResult.added} photos`);
    } catch (error) {
      console.error('[DragDropManager] Failed to create collection:', error);

      // Check for capacity limit error
      if (error.message && error.message.includes('limit')) {
        this.app.components.toast.show(
          'Collection limit reached (2,048 photos maximum)',
          { icon: '⚠️', duration: 5000 }
        );
      } else {
        this.app.components.toast.show(
          'Failed to create collection',
          { icon: '⚠️', duration: 3000 }
        );
      }
    }
  }

  /**
   * Add photos to existing collection
   */
  async addPhotosToCollection(collectionId, photoIds) {
    console.log('[DragDropManager] Adding photos to collection:', collectionId, photoIds);
    try {
      // Get collection name for toast message
      const collections = this.app.components.collectionsSidebar?.collections || [];
      const collection = collections.find(c => c.id === collectionId);
      const collectionName = collection?.name || 'collection';

      // Add photos
      const result = await this.app.api.post(`/api/collections/${collectionId}/photos`, {
        photoIds: photoIds
      });

      // Reload collections sidebar to update counts
      if (this.app.components.collectionsSidebar) {
        await this.app.components.collectionsSidebar.loadCollections();
        this.app.components.collectionsSidebar.render();
      }

      this.app.components.toast.show(
        `Added ${result.added} photo${result.added !== 1 ? 's' : ''} to "${collectionName}"`,
        { icon: '✓', duration: 2000 }
      );

      // Clear text selection after successful drop
      this.app.components.photoSelection.clearSelection();

      console.log(`[DragDropManager] Added ${result.added} photos to collection ${collectionId}`);
    } catch (error) {
      console.error('[DragDropManager] Failed to add photos to collection:', error);

      // Check for capacity limit error
      if (error.message && error.message.includes('limit')) {
        this.app.components.toast.show(
          'Collection limit reached (2,048 photos maximum)',
          { icon: '⚠️', duration: 5000 }
        );
      } else {
        this.app.components.toast.show(
          'Failed to add photos to collection',
          { icon: '⚠️', duration: 3000 }
        );
      }
    }
  }

  /**
   * Clear drop target visual feedback
   */
  clearDropTargetHighlight() {
    document.querySelectorAll('.drop-target').forEach(el => {
      el.classList.remove('drop-target');
    });
  }

  /**
   * Reinitialize handlers after sidebar re-render
   * Called by CollectionsSidebar component
   */
  reinit() {
    this.attachDropZoneHandlers();
  }
}
