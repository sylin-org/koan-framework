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
        </div>
      </div>
    `;
  }

  /**
   * Render types list (grid or empty state)
   */
  renderTypesList() {
    // Show loading skeleton while data is being fetched
    if (this.isLoading) {
      return LoadingState.render('card', { count: 6 });
    }

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
    return `
      <a href="#/source-types/${type.id}/view" class="type-card-link">
        <div class="type-card card-lift" data-type-id="${type.id}">
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
        </div>
      </a>
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
   * Load types from API
   */
  async loadTypes() {
    this.isLoading = true;
    try {
      this.types = await this.api.getSourceTypes();
      this.applyFilters();
    } catch (error) {
      console.error('Failed to load source types:', error);
      this.toast.error('Failed to load source types');
      this.types = [];
      this.filteredTypes = [];
    } finally {
      this.isLoading = false;
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

    // No card action buttons - cards are now fully clickable links
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
    `;

    this.attachEventHandlers(container);
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
   * Open AI Create modal
   */
  async openAICreateModal() {
    if (!this.aiCreateModal) {
      this.aiCreateModal = new AICreateTypeModal('source', this.api, this.toast);
    }

    try {
      const createdType = await this.aiCreateModal.openAICreate();
      console.log('AI Create completed, returned:', createdType);

      // Always reload types after AI create attempt
      // (Backend might have created it even if modal didn't return proper data)
      await this.loadTypes();

      // Refresh view
      const container = document.querySelector('#app');
      if (container) this.updateView(container);

    } catch (error) {
      console.error('AI Create modal error:', error);
      // Still reload in case backend succeeded but frontend failed
      await this.loadTypes();
      const container = document.querySelector('#app');
      if (container) this.updateView(container);
    }
  }

  /**
   * Navigate to create view
   */
  navigateToCreate() {
    this.eventBus.emit('navigate', 'source-type-create');
  }

  /**
   * Open type in detail panel (view mode)
   */
  navigateToView(id) {
    // Route-based full-page view
    this.eventBus.emit('navigate', `source-type-view:${id}`);
  }

  /**
   * Open type in detail panel (edit mode)
   */
  navigateToEdit(id) {
    // Route-based full-page edit
    this.eventBus.emit('navigate', `source-type-edit:${id}`);
  }

  /**
   * Format date for display
   */
  formatDate(dateString) {
    if (!dateString) return 'N/A';
    const date = new Date(dateString);
    if (isNaN(date.getTime())) return 'Invalid date';
    
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);
    
    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;
    
    return date.toLocaleDateString('en-US', { 
      year: 'numeric', 
      month: 'short', 
      day: 'numeric' 
    });
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
