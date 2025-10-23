/**
 * SourceTypesManager - Full CRUD interface for Source Types (Input Types)
 * Features:
 * - Grid layout with type cards
 * - Search/filter by name, tags
 * - Bulk selection and operations
 * - Per-card actions: View, Edit, Delete
 * - Integration with TypeFormView and AICreateTypeModal
 * - Uses standardized EmptyState and LoadingState components
 */
import { TypeFormView } from './TypeFormView.js';
import { AICreateTypeModal } from './AICreateTypeModal.js';
import { EmptyState } from './EmptyState.js';
import { LoadingState } from './LoadingState.js';
import { PageHeader } from './PageHeader.js';
import { SearchFilter } from './SearchFilter.js';

export class SourceTypesManager {
  constructor(api, eventBus, toast, router) {
    this.api = api;
    this.eventBus = eventBus;
    this.toast = toast;
    this.router = router;
    this.types = [];
    this.filteredTypes = [];
    this.selectedIds = new Set();
    this.searchQuery = '';
    this.sortBy = 'name';
    this.sortDirection = 'asc';
    this.isLoading = false;
    this.aiCreateModal = null;
    this.pageHeader = new PageHeader(router, eventBus);
    this.searchFilter = new SearchFilter(eventBus, {
      searchPlaceholder: 'Search source types by name or tags...',
      sortOptions: [
        { value: 'name', label: 'Name' },
        { value: 'updated', label: 'Recently Updated' },
        { value: 'created', label: 'Recently Created' }
      ],
      defaultSort: 'name',
      defaultSortDirection: 'asc'
    });
  }

  /**
   * Render the source types list view
   */
  async render() {
    await this.loadTypes();

    return `
      <div class="types-manager">
        ${this.renderHeader()}
        ${this.renderToolbar()}
        ${this.renderTypesList()}
        ${this.renderBulkActionsBar()}
      </div>
    `;
  }

  /**
   * Render header with title and primary actions
   */
  renderHeader() {
    return this.pageHeader.render({
      title: 'Source Types',
      subtitle: 'Manage source type definitions that determine what data is extracted from input documents.',
      breadcrumbs: [
        {
          label: 'Home',
          path: '#/',
          icon: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>'
        },
        {
          label: 'Source Types',
          path: '#/source-types'
        }
      ],
      actions: [
        {
          label: 'Create Type',
          action: 'create',
          variant: 'secondary',
          icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
        },
        {
          label: 'AI Create',
          action: 'ai-create',
          variant: 'primary',
          icon: '<circle cx="12" cy="12" r="3"></circle><path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>'
        }
      ]
    });
  }

  /**
   * Render toolbar with search and filters
   */
  renderToolbar() {
    return `
      <div class="types-manager-toolbar">
        ${this.searchFilter.render()}
        <div class="types-manager-stats">
          <span class="stat-badge">${this.filteredTypes.length} ${this.filteredTypes.length === 1 ? 'type' : 'types'}</span>
          ${this.selectedIds.size > 0 ? `<span class="stat-badge stat-selected">${this.selectedIds.size} selected</span>` : ''}
        </div>
      </div>
    `;
  }

  /**
   * Render types list (grid or empty state)
   */
  renderTypesList() {
    if (this.types.length === 0) {
      return this.renderEmptyState();
    }

    if (this.filteredTypes.length === 0) {
      return this.renderNoResults();
    }

    return `
      <div class="types-grid">
        ${this.filteredTypes.map(type => this.renderTypeCard(type)).join('')}
      </div>
    `;
  }

  /**
   * Render a single type card
   */
  renderTypeCard(type) {
    const isSelected = this.selectedIds.has(type.id);

    return `
      <div class="type-card card-lift ${isSelected ? 'selected' : ''}" data-type-id="${type.id}">
        <div class="type-card-select">
          <input
            type="checkbox"
            class="bulk-select-checkbox"
            ${isSelected ? 'checked' : ''}
            data-checkbox="${type.id}"
          />
        </div>

        <div class="type-card-content">
          <div class="type-card-header">
            <h3 class="type-card-name">${this.escapeHtml(type.name)}</h3>
            <span class="type-badge type-badge-source">Source</span>
          </div>

          <p class="type-card-description">
            ${this.escapeHtml(type.description || 'No description provided')}
          </p>

          ${type.tags && type.tags.length > 0 ? `
            <div class="type-card-tags">
              ${type.tags.slice(0, 3).map(tag => `
                <span class="type-card-tag">${this.escapeHtml(tag)}</span>
              `).join('')}
              ${type.tags.length > 3 ? `<span class="type-card-tag-more">+${type.tags.length - 3} more</span>` : ''}
            </div>
          ` : ''}

          <div class="type-card-meta">
            <span class="type-card-meta-item">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
              </svg>
              ${type.usageCount || 0} documents
            </span>
          </div>
        </div>

        <div class="type-card-actions">
          <button
            class="type-card-action-btn btn-press"
            title="View"
            data-action="view"
            data-id="${type.id}"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path>
              <circle cx="12" cy="12" r="3"></circle>
            </svg>
          </button>
          <button
            class="type-card-action-btn btn-press"
            title="Edit"
            data-action="edit"
            data-id="${type.id}"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path>
              <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path>
            </svg>
          </button>
          <button
            class="type-card-action-btn action-delete btn-press"
            title="Delete"
            data-action="delete"
            data-id="${type.id}"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
          </button>
        </div>
      </div>
    `;
  }

  /**
   * Render empty state (no types exist)
   */
  renderEmptyState() {
    return EmptyState.forSourceTypes();
  }

  /**
   * Render no search results state
   */
  renderNoResults() {
    return EmptyState.forSearchResults();
  }

  /**
   * Render bulk actions bar
   */
  renderBulkActionsBar() {
    return `
      <div class="bulk-actions-bar ${this.selectedIds.size > 0 ? 'visible' : ''}" data-bulk-bar>
        <span class="bulk-selection-count">
          ${this.selectedIds.size} ${this.selectedIds.size === 1 ? 'type' : 'types'} selected
        </span>
        <button class="btn" data-action="deselect-all">
          Deselect All
        </button>
        <button class="btn" data-action="bulk-delete">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="3 6 5 6 21 6"></polyline>
            <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
          </svg>
          Delete Selected
        </button>
      </div>
    `;
  }

  /**
   * Load types from API
   */
  async loadTypes() {
    try {
      this.types = await this.api.getSourceTypes();
      this.applyFilters();
    } catch (error) {
      console.error('Failed to load source types:', error);
      this.toast.error('Failed to load source types');
      this.types = [];
      this.filteredTypes = [];
    }
  }

  /**
   * Apply search filters and sorting
   */
  applyFilters() {
    const query = this.searchQuery.toLowerCase().trim();

    // Filter
    let filtered = [...this.types];
    if (query) {
      filtered = filtered.filter(type => {
        // Search in name
        if (type.name?.toLowerCase().includes(query)) return true;

        // Search in description
        if (type.description?.toLowerCase().includes(query)) return true;

        // Search in tags
        if (type.tags?.some(tag => tag.toLowerCase().includes(query))) return true;

        return false;
      });
    }

    // Sort
    filtered.sort((a, b) => {
      let comparison = 0;

      switch (this.sortBy) {
        case 'name':
          comparison = (a.name || '').localeCompare(b.name || '');
          break;
        case 'updated':
          comparison = new Date(b.updatedAt || b.createdAt || 0) - new Date(a.updatedAt || a.createdAt || 0);
          break;
        case 'created':
          comparison = new Date(b.createdAt || 0) - new Date(a.createdAt || 0);
          break;
      }

      return this.sortDirection === 'asc' ? comparison : -comparison;
    });

    this.filteredTypes = filtered;
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    if (!container) return;

    // Attach PageHeader event handlers
    this.pageHeader.attachEventHandlers(container);

    // Listen for PageHeader action events
    const headerActionHandler = (action) => {
      switch (action) {
        case 'create':
          this.navigateToCreate();
          break;
        case 'ai-create':
          this.openAICreateModal();
          break;
      }
    };
    this.eventBus.on('page-header-action', headerActionHandler);

    // Attach SearchFilter event handlers
    this.searchFilter.attachEventHandlers(container);

    // Listen for search/filter changes
    const searchFilterHandler = (state) => {
      this.searchQuery = state.search || '';
      this.sortBy = state.sortBy || 'name';
      this.sortDirection = state.sortDirection || 'asc';
      this.applyFilters();
      this.updateView(container);
    };
    this.eventBus.on('search-filter-changed', searchFilterHandler);

    // Bulk selection checkboxes
    const checkboxes = container.querySelectorAll('[data-checkbox]');
    checkboxes.forEach(checkbox => {
      checkbox.addEventListener('change', (e) => {
        const typeId = checkbox.getAttribute('data-checkbox');
        if (e.target.checked) {
          this.selectedIds.add(typeId);
        } else {
          this.selectedIds.delete(typeId);
        }
        this.updateBulkActionsBar(container);
        this.updateCardSelection(container, typeId);
      });
    });

    // Card actions
    const viewBtns = container.querySelectorAll('[data-action="view"]');
    viewBtns.forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-id');
        this.navigateToView(id);
      });
    });

    const editBtns = container.querySelectorAll('[data-action="edit"]');
    editBtns.forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-id');
        this.navigateToEdit(id);
      });
    });

    const deleteBtns = container.querySelectorAll('[data-action="delete"]');
    deleteBtns.forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-id');
        this.deleteType(id);
      });
    });

    // Bulk actions
    const deselectAllBtn = container.querySelector('[data-action="deselect-all"]');
    if (deselectAllBtn) {
      deselectAllBtn.addEventListener('click', () => {
        this.selectedIds.clear();
        this.updateView(container);
      });
    }

    const bulkDeleteBtn = container.querySelector('[data-action="bulk-delete"]');
    if (bulkDeleteBtn) {
      bulkDeleteBtn.addEventListener('click', () => {
        this.bulkDeleteTypes();
      });
    }
  }

  /**
   * Update view after data changes
   */
  updateView(container) {
    const typesManager = container.querySelector('.types-manager');
    if (!typesManager) return;

    typesManager.innerHTML = `
      ${this.renderHeader()}
      ${this.renderToolbar()}
      ${this.renderTypesList()}
      ${this.renderBulkActionsBar()}
    `;

    this.attachEventHandlers(container);
  }

  /**
   * Update bulk actions bar visibility
   */
  updateBulkActionsBar(container) {
    const bulkBar = container.querySelector('[data-bulk-bar]');
    if (!bulkBar) return;

    if (this.selectedIds.size > 0) {
      bulkBar.classList.add('visible');
      bulkBar.innerHTML = `
        <span class="bulk-selection-count">
          ${this.selectedIds.size} ${this.selectedIds.size === 1 ? 'type' : 'types'} selected
        </span>
        <button class="btn" data-action="deselect-all">
          Deselect All
        </button>
        <button class="btn" data-action="bulk-delete">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="3 6 5 6 21 6"></polyline>
            <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
          </svg>
          Delete Selected
        </button>
      `;
    } else {
      bulkBar.classList.remove('visible');
    }

    // Re-attach bulk action handlers
    const deselectAllBtn = bulkBar.querySelector('[data-action="deselect-all"]');
    if (deselectAllBtn) {
      deselectAllBtn.addEventListener('click', () => {
        this.selectedIds.clear();
        this.updateView(container.closest('.types-manager').parentElement);
      });
    }

    const bulkDeleteBtn = bulkBar.querySelector('[data-action="bulk-delete"]');
    if (bulkDeleteBtn) {
      bulkDeleteBtn.addEventListener('click', () => {
        this.bulkDeleteTypes();
      });
    }

    // Update stats in toolbar
    const statsContainer = container.querySelector('.types-manager-stats');
    if (statsContainer) {
      statsContainer.innerHTML = `
        <span class="stat-badge">${this.filteredTypes.length} ${this.filteredTypes.length === 1 ? 'type' : 'types'}</span>
        ${this.selectedIds.size > 0 ? `<span class="stat-badge stat-selected">${this.selectedIds.size} selected</span>` : ''}
      `;
    }
  }

  /**
   * Update card selection styling
   */
  updateCardSelection(container, typeId) {
    const card = container.querySelector(`[data-type-id="${typeId}"]`);
    if (!card) return;

    if (this.selectedIds.has(typeId)) {
      card.classList.add('selected');
    } else {
      card.classList.remove('selected');
    }
  }

  /**
   * Delete a single type
   */
  async deleteType(id) {
    const type = this.types.find(t => t.id === id);
    if (!type) return;

    const confirmed = confirm(`Are you sure you want to delete "${type.name}"? This action cannot be undone.`);
    if (!confirmed) return;

    try {
      await this.api.deleteSourceType(id);
      this.toast.success('Source type deleted successfully');

      // Remove from local state
      this.types = this.types.filter(t => t.id !== id);
      this.selectedIds.delete(id);
      this.applyFilters();

      // Refresh view
      const container = document.querySelector('#app');
      if (container) this.updateView(container);

    } catch (error) {
      console.error('Failed to delete type:', error);
      this.toast.error('Failed to delete type');
    }
  }

  /**
   * Bulk delete selected types
   */
  async bulkDeleteTypes() {
    if (this.selectedIds.size === 0) return;

    const confirmed = confirm(
      `Are you sure you want to delete ${this.selectedIds.size} ${this.selectedIds.size === 1 ? 'type' : 'types'}? This action cannot be undone.`
    );
    if (!confirmed) return;

    try {
      await this.api.bulkDeleteSourceTypes(Array.from(this.selectedIds));
      this.toast.success(`${this.selectedIds.size} ${this.selectedIds.size === 1 ? 'type' : 'types'} deleted successfully`);

      // Remove from local state
      this.types = this.types.filter(t => !this.selectedIds.has(t.id));
      this.selectedIds.clear();
      this.applyFilters();

      // Refresh view
      const container = document.querySelector('#app');
      if (container) this.updateView(container);

    } catch (error) {
      console.error('Failed to bulk delete types:', error);
      this.toast.error('Failed to delete types');
    }
  }

  /**
   * Open AI Create modal
   */
  async openAICreateModal() {
    if (!this.aiCreateModal) {
      this.aiCreateModal = new AICreateTypeModal('source', this.api, this.toast);
    }

    try {
      const createdType = await this.aiCreateModal.openAICreate();

      if (createdType) {
        // Reload types
        await this.loadTypes();

        // Refresh view
        const container = document.querySelector('#app');
        if (container) this.updateView(container);
      }
    } catch (error) {
      console.error('AI Create modal error:', error);
    }
  }

  /**
   * Navigate to create view
   */
  navigateToCreate() {
    this.eventBus.emit('navigate', 'source-type-create');
  }

  /**
   * Navigate to view mode
   */
  navigateToView(id) {
    this.eventBus.emit('navigate', 'source-type-view', { id });
  }

  /**
   * Navigate to edit mode
   */
  navigateToEdit(id) {
    this.eventBus.emit('navigate', 'source-type-edit', { id });
  }

  /**
   * Escape HTML to prevent XSS
   */
  escapeHtml(text) {
    if (text == null) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
