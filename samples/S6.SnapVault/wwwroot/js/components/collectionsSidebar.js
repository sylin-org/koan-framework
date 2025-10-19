/**
 * CollectionsSidebar Component
 * Manages collection list in left sidebar with drag-and-drop support
 * Follows existing vanilla JS component architecture
 */

import { escapeHtml } from '../utils/html.js';
import { confirmDeleteCollection } from '../utils/dialogs.js';

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
      // Sort by ID descending (newest first - GUID v7 has timestamp embedded)
      this.collections = (response || []).sort((a, b) => b.id.localeCompare(a.id));
      console.log(`[CollectionsSidebar] Loaded ${this.collections.length} collections`);
    } catch (error) {
      console.error('[CollectionsSidebar] Failed to load collections:', error);
      this.app.components.toast.show('Failed to load collections', { icon: '⚠️', duration: 3000 });
      this.collections = [];
    }
  }

  /**
   * Render collections section in the sidebar
   * Borderless design matching photo information panel
   */
  render() {
    const sidebar = document.querySelector('.sidebar-left');
    if (!sidebar) {
      console.error('[CollectionsSidebar] Sidebar container not found');
      return;
    }

    // Find or create collections section (NOT panel!)
    let collectionsSection = sidebar.querySelector('.sidebar-section.collections-section');

    if (!collectionsSection) {
      // Create section after library section
      const librarySection = sidebar.querySelector('.sidebar-section.library-section');
      collectionsSection = document.createElement('section');
      collectionsSection.className = 'sidebar-section collections-section';

      if (librarySection && librarySection.nextSibling) {
        librarySection.parentNode.insertBefore(collectionsSection, librarySection.nextSibling);
      } else {
        sidebar.appendChild(collectionsSection);
      }
    }

    // Render section content - no boxes, clean structure
    collectionsSection.innerHTML = `
      <div class="section-header-row">
        <h2 class="section-header">COLLECTIONS</h2>
        <button class="btn-new-collection" title="New collection">
          <svg class="icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="12" y1="5" x2="12" y2="19"></line>
            <line x1="5" y1="12" x2="19" y2="12"></line>
          </svg>
        </button>
      </div>
      <nav class="section-items">
        ${this.collections.length === 0 ? '<p class="empty-state">No collections yet</p>' : ''}
        ${this.collections.map(c => this.renderCollectionItem(c)).join('')}
      </nav>
    `;

    this.attachEventHandlers();
  }

  renderCollectionItem(collection) {
    const isActive = this.activeViewId === collection.id;
    const nearLimit = collection.photoCount > 1800;

    return `
      <div class="sidebar-item collection-item ${isActive ? 'active' : ''}"
           data-collection-id="${collection.id}"
           data-droppable="true"
           role="button"
           tabindex="0">
        <span class="item-label">${escapeHtml(collection.name)}</span>
        <span class="item-badge${nearLimit ? ' near-limit' : ''}">${collection.photoCount}</span>
      </div>
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
        this.selectView(e.currentTarget.dataset.collectionId);
      });

      // Note: Rename and delete now happen in main content header, not sidebar
    });
  }

  selectView(viewId) {
    this.activeViewId = viewId;
    this.render();

    // Update main content
    if (this.app.components.collectionView) {
      this.app.components.collectionView.setView(viewId);
    }

    // Clear library section active state
    document.querySelectorAll('.library-section .sidebar-item').forEach(i => i.classList.remove('active'));

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

    if (!confirmDeleteCollection(collection.name, collection.photoCount)) return;

    try {
      await this.app.api.delete(`/api/collections/${collectionId}`);

      // If we're viewing this collection, switch to All Photos
      if (this.activeViewId === collectionId) {
        this.activeViewId = 'all-photos';
        if (this.app.components.collectionView) {
          this.app.components.collectionView.setView('all-photos');
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

}
