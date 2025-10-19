/**
 * CollectionsSidebar Component
 * Manages collection list in left sidebar with drag-and-drop support
 * Follows existing vanilla JS component architecture
 */

export class CollectionsSidebar {
  constructor(app) {
    this.app = app;
    this.collections = [];
    this.activeViewId = 'all-photos'; // 'all-photos' | 'favorites' | collectionId
  }

  async init() {
    await this.loadCollections();
    this.render();
    this.attachEventHandlers();
  }

  async loadCollections() {
    try {
      const response = await this.app.api.get('/api/collections');
      this.collections = response || [];
      console.log(`[CollectionsSidebar] Loaded ${this.collections.length} collections`);
    } catch (error) {
      console.error('[CollectionsSidebar] Failed to load collections:', error);
      this.app.components.toast.show('Failed to load collections', { icon: '⚠️', duration: 3000 });
      this.collections = [];
    }
  }

  /**
   * Render collections section in the sidebar
   * Updates existing library panel instead of replacing
   */
  render() {
    const sidebar = document.querySelector('.sidebar-left');
    if (!sidebar) {
      console.error('[CollectionsSidebar] Sidebar container not found');
      return;
    }

    // Find or create collections panel
    let collectionsPanel = sidebar.querySelector('.collections-panel');

    if (!collectionsPanel) {
      // Create panel after library panel
      const libraryPanel = sidebar.querySelector('.library-panel');
      collectionsPanel = document.createElement('div');
      collectionsPanel.className = 'panel collections-panel';

      if (libraryPanel && libraryPanel.nextSibling) {
        libraryPanel.parentNode.insertBefore(collectionsPanel, libraryPanel.nextSibling);
      } else {
        sidebar.appendChild(collectionsPanel);
      }
    }

    // Render panel content
    collectionsPanel.innerHTML = `
      <div class="panel-header">
        <h3>Collections</h3>
        <button class="btn-new-collection btn-icon" title="New collection">
          <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="12" y1="5" x2="12" y2="19"></line>
            <line x1="5" y1="12" x2="19" y2="12"></line>
          </svg>
        </button>
      </div>
      <div class="collections-list">
        ${this.collections.length === 0 ? '<p class="empty-state">No collections yet</p>' : ''}
        ${this.collections.map(c => this.renderCollectionItem(c)).join('')}
      </div>
    `;

    this.attachEventHandlers();
  }

  renderCollectionItem(collection) {
    const isActive = this.activeViewId === collection.id;
    const percentage = (collection.photoCount / 2048) * 100;

    return `
      <button class="library-item collection-item ${isActive ? 'active' : ''}"
              data-collection-id="${collection.id}"
              data-droppable="true">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
        </svg>
        <span class="label collection-name" contenteditable="false">${this.escapeHtml(collection.name)}</span>
        <span class="badge">${collection.photoCount}</span>
        <button class="btn-delete-collection" data-collection-id="${collection.id}" title="Delete collection" aria-label="Delete collection">×</button>
        ${collection.photoCount > 1800 ? `
          <div class="capacity-indicator" title="${collection.photoCount} / 2048 photos">
            <div class="capacity-bar">
              <div class="capacity-fill ${percentage > 90 ? 'warning' : ''}" style="width: ${percentage}%"></div>
            </div>
          </div>
        ` : ''}
      </button>
    `;
  }

  attachEventHandlers() {
    // New collection button
    const btnNew = document.querySelector('.btn-new-collection');
    if (btnNew) {
      btnNew.replaceWith(btnNew.cloneNode(true)); // Remove old handlers
      document.querySelector('.btn-new-collection').addEventListener('click', () => {
        this.createCollection();
      });
    }

    // Collection item clicks
    document.querySelectorAll('.collection-item').forEach(item => {
      // Click to select collection view
      item.addEventListener('click', (e) => {
        if (!e.target.closest('.btn-delete-collection') && !e.target.closest('.collection-name[contenteditable="true"]')) {
          this.selectView(e.currentTarget.dataset.collectionId);
        }
      });

      // Delete button
      const deleteBtn = item.querySelector('.btn-delete-collection');
      if (deleteBtn) {
        deleteBtn.addEventListener('click', async (e) => {
          e.stopPropagation();
          await this.deleteCollection(e.currentTarget.dataset.collectionId);
        });
      }

      // Double-click collection name to rename
      const nameEl = item.querySelector('.collection-name');
      if (nameEl) {
        nameEl.addEventListener('dblclick', (e) => {
          e.stopPropagation();
          this.startRename(e.currentTarget);
        });
      }
    });
  }

  selectView(viewId) {
    this.activeViewId = viewId;
    this.render();

    // Update main content
    if (this.app.components.collectionView) {
      this.app.components.collectionView.load(viewId);
    }

    // Clear library panel active state
    document.querySelectorAll('.library-panel .library-item').forEach(i => i.classList.remove('active'));

    console.log(`[CollectionsSidebar] Selected view: ${viewId}`);
  }

  async createCollection(name = null, photoIds = []) {
    const collectionName = name || prompt('Collection name:');
    if (!collectionName || collectionName.trim() === '') return;

    try {
      const response = await this.app.api.post('/api/collections', {
        name: collectionName.trim()
      });

      // If photos provided (from drag-to-create), add them
      if (photoIds.length > 0) {
        await this.app.api.post(`/api/collections/${response.id}/photos`, {
          photoIds: photoIds
        });
      }

      await this.loadCollections();
      this.render();
      this.app.components.toast.show(`Collection "${collectionName}" created`, {
        icon: '📁',
        duration: 2000
      });
    } catch (error) {
      console.error('[CollectionsSidebar] Failed to create collection:', error);
      this.app.components.toast.show('Failed to create collection', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  async deleteCollection(collectionId) {
    const collection = this.collections.find(c => c.id === collectionId);
    if (!collection) return;

    const confirmed = confirm(
      `Delete collection "${collection.name}"?\n\n` +
      `${collection.photoCount} photo${collection.photoCount !== 1 ? 's' : ''} will remain in your library.`
    );

    if (!confirmed) return;

    try {
      await this.app.api.delete(`/api/collections/${collectionId}`);

      // If we're viewing this collection, switch to All Photos
      if (this.activeViewId === collectionId) {
        this.activeViewId = 'all-photos';
        if (this.app.components.collectionView) {
          this.app.components.collectionView.load('all-photos');
        }
      }

      await this.loadCollections();
      this.render();
      this.app.components.toast.show(`Collection "${collection.name}" deleted`, {
        icon: '🗑️',
        duration: 2000
      });
    } catch (error) {
      console.error('[CollectionsSidebar] Failed to delete collection:', error);
      this.app.components.toast.show('Failed to delete collection', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  /**
   * Programmatically start rename mode for a collection by ID
   * Used after instant collection creation to trigger auto-rename
   */
  startRenameById(collectionId) {
    // Find the collection item by ID
    const collectionItem = document.querySelector(`.collection-item[data-collection-id="${collectionId}"]`);

    if (!collectionItem) {
      console.warn('[CollectionsSidebar] Collection item not found for rename:', collectionId);
      return;
    }

    // Find the name element
    const nameElement = collectionItem.querySelector('.collection-name');

    if (!nameElement) {
      console.warn('[CollectionsSidebar] Name element not found');
      return;
    }

    // Use existing rename logic
    this.startRename(nameElement);

    console.log('[CollectionsSidebar] Auto-started rename for collection:', collectionId);
  }

  /**
   * Start inline rename mode for a collection
   * Handles Enter (save), Escape (cancel), and blur (save) events
   */
  startRename(nameElement) {
    const collectionItem = nameElement.closest('.collection-item');
    const collectionId = collectionItem.dataset.collectionId;
    const originalName = nameElement.textContent;

    nameElement.contentEditable = true;
    nameElement.focus();

    // Select all text
    const range = document.createRange();
    range.selectNodeContents(nameElement);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);

    const finishRename = async () => {
      const newName = nameElement.textContent.trim();
      nameElement.contentEditable = false;

      // Only save if name actually changed
      if (newName && newName !== originalName) {
        try {
          await this.app.api.put(`/api/collections/${collectionId}`, {
            name: newName
          });
          await this.loadCollections();
          this.render();
          this.app.components.toast.show(`Renamed to "${newName}"`, {
            icon: '✏️',
            duration: 2000
          });
        } catch (error) {
          console.error('[CollectionsSidebar] Failed to rename collection:', error);
          nameElement.textContent = originalName;
          this.app.components.toast.show('Failed to rename collection', {
            icon: '⚠️',
            duration: 3000
          });
        }
      } else {
        // No change or empty - revert to original
        nameElement.textContent = originalName;
      }
    };

    // Save on blur (user clicks away)
    nameElement.addEventListener('blur', finishRename, { once: true });

    // Handle keyboard shortcuts
    nameElement.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        nameElement.blur(); // Triggers finishRename via blur event
      } else if (e.key === 'Escape') {
        nameElement.textContent = originalName;
        nameElement.blur();
      }
    });
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
