/**
 * SearchFilter - Unified search and filter component
 *
 * Design Pattern: Consistent filtering across all entity lists
 * Features:
 * - Debounced search input
 * - Multiple filter dropdowns
 * - Sort controls with direction toggle
 * - Responsive layout
 * - EventBus integration
 * - Clear all filters
 */
export class SearchFilter {
  /**
   * @param {EventBus} eventBus - Event bus for communication
   * @param {Object} options - Configuration
   */
  constructor(eventBus, options = {}) {
    this.eventBus = eventBus;
    this.options = {
      searchPlaceholder: 'Search...',
      filters: [], // [{id, label, options: [{value, label}]}]
      sortOptions: [], // [{value, label}]
      defaultSort: null,
      defaultSortDirection: 'asc',
      compact: false,
      ...options
    };

    this.state = {
      searchQuery: '',
      activeFilters: {}, // {filterId: value}
      sortBy: this.options.defaultSort,
      sortDirection: this.options.defaultSortDirection
    };

    this.searchDebounceTimer = null;
    this.debounceDelay = 300;
  }

  /**
   * Render the search filter component
   * @returns {string} HTML string
   */
  render() {
    const { compact } = this.options;

    return `
      <div class="search-filter ${compact ? 'search-filter-compact' : ''}" data-search-filter>
        <div class="search-filter-main">
          <!-- Search Input -->
          ${this.renderSearchInput()}

          <!-- Filter Dropdowns -->
          ${this.renderFilters()}

          <!-- Sort Controls -->
          ${this.renderSort()}

          <!-- Clear Filters -->
          ${this.hasActiveFilters() ? this.renderClearButton() : ''}
        </div>

        <!-- Active Filter Tags -->
        ${this.hasActiveFilters() ? this.renderActiveFilters() : ''}
      </div>
    `;
  }

  /**
   * Render search input
   */
  renderSearchInput() {
    return `
      <div class="search-filter-search">
        <div class="search-input-wrapper">
          <svg class="search-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"></circle>
            <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
          </svg>
          <input
            type="text"
            class="search-input"
            placeholder="${this.escapeHtml(this.options.searchPlaceholder)}"
            value="${this.escapeHtml(this.state.searchQuery)}"
            data-search-input
            aria-label="Search"
          />
          ${this.state.searchQuery ? `
            <button class="search-clear-btn" data-clear-search aria-label="Clear search">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          ` : ''}
        </div>
      </div>
    `;
  }

  /**
   * Render filter dropdowns
   */
  renderFilters() {
    if (this.options.filters.length === 0) return '';

    return `
      <div class="search-filter-filters">
        ${this.options.filters.map(filter => this.renderFilterDropdown(filter)).join('')}
      </div>
    `;
  }

  /**
   * Render single filter dropdown
   */
  renderFilterDropdown(filter) {
    const activeValue = this.state.activeFilters[filter.id];
    const activeOption = filter.options.find(opt => opt.value === activeValue);
    const label = activeOption ? activeOption.label : filter.label;
    const isActive = !!activeValue;

    return `
      <div class="filter-dropdown ${isActive ? 'active' : ''}" data-filter="${filter.id}">
        <button class="filter-dropdown-trigger" data-filter-trigger="${filter.id}" aria-haspopup="true" aria-expanded="false">
          <span class="filter-label">${this.escapeHtml(label)}</span>
          <svg class="filter-arrow" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </button>
        <div class="filter-dropdown-menu" data-filter-menu="${filter.id}" role="menu">
          <button class="filter-option ${!activeValue ? 'active' : ''}" data-filter-option="${filter.id}" data-value="" role="menuitem">
            <span>All ${this.escapeHtml(filter.label)}</span>
            ${!activeValue ? '<svg class="check-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"></polyline></svg>' : ''}
          </button>
          ${filter.options.map(option => `
            <button class="filter-option ${activeValue === option.value ? 'active' : ''}" data-filter-option="${filter.id}" data-value="${this.escapeHtml(option.value)}" role="menuitem">
              <span>${this.escapeHtml(option.label)}</span>
              ${activeValue === option.value ? '<svg class="check-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"></polyline></svg>' : ''}
            </button>
          `).join('')}
        </div>
      </div>
    `;
  }

  /**
   * Render sort controls
   */
  renderSort() {
    if (this.options.sortOptions.length === 0) return '';

    const activeSortOption = this.options.sortOptions.find(opt => opt.value === this.state.sortBy);
    const sortLabel = activeSortOption ? activeSortOption.label : 'Sort by';

    return `
      <div class="search-filter-sort">
        <div class="sort-dropdown" data-sort-dropdown>
          <button class="sort-dropdown-trigger" data-sort-trigger aria-haspopup="true" aria-expanded="false">
            <span class="sort-label">${this.escapeHtml(sortLabel)}</span>
            <svg class="sort-arrow" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="6 9 12 15 18 9"></polyline>
            </svg>
          </button>
          <div class="sort-dropdown-menu" data-sort-menu role="menu">
            ${this.options.sortOptions.map(option => `
              <button class="sort-option ${this.state.sortBy === option.value ? 'active' : ''}" data-sort-option data-value="${this.escapeHtml(option.value)}" role="menuitem">
                <span>${this.escapeHtml(option.label)}</span>
                ${this.state.sortBy === option.value ? '<svg class="check-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"></polyline></svg>' : ''}
              </button>
            `).join('')}
          </div>
        </div>
        <button class="sort-direction-btn" data-sort-direction aria-label="Toggle sort direction" title="${this.state.sortDirection === 'asc' ? 'Ascending' : 'Descending'}">
          ${this.state.sortDirection === 'asc' ? `
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <polyline points="5 12 12 5 19 12"></polyline>
            </svg>
          ` : `
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <polyline points="19 12 12 19 5 12"></polyline>
            </svg>
          `}
        </button>
      </div>
    `;
  }

  /**
   * Render clear all filters button
   */
  renderClearButton() {
    return `
      <button class="search-filter-clear-btn" data-clear-all>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="18" y1="6" x2="6" y2="18"></line>
          <line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
        <span>Clear</span>
      </button>
    `;
  }

  /**
   * Render active filter tags
   */
  renderActiveFilters() {
    const tags = [];

    // Search query tag
    if (this.state.searchQuery) {
      tags.push({
        type: 'search',
        label: `Search: "${this.state.searchQuery}"`,
        clearAction: 'clear-search'
      });
    }

    // Filter tags
    Object.entries(this.state.activeFilters).forEach(([filterId, value]) => {
      if (!value) return;

      const filter = this.options.filters.find(f => f.id === filterId);
      if (!filter) return;

      const option = filter.options.find(opt => opt.value === value);
      if (!option) return;

      tags.push({
        type: 'filter',
        label: `${filter.label}: ${option.label}`,
        clearAction: `clear-filter-${filterId}`
      });
    });

    if (tags.length === 0) return '';

    return `
      <div class="search-filter-tags">
        ${tags.map(tag => `
          <span class="filter-tag" data-tag="${tag.clearAction}">
            <span class="filter-tag-label">${this.escapeHtml(tag.label)}</span>
            <button class="filter-tag-remove" data-remove-tag="${tag.clearAction}" aria-label="Remove ${tag.type} filter">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </span>
        `).join('')}
      </div>
    `;
  }

  /**
   * Check if there are any active filters
   */
  hasActiveFilters() {
    return this.state.searchQuery || Object.values(this.state.activeFilters).some(v => v);
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    if (!container) return;

    // Search input
    const searchInput = container.querySelector('[data-search-input]');
    if (searchInput) {
      searchInput.addEventListener('input', (e) => this.handleSearchInput(e.target.value));
    }

    // Clear search
    const clearSearchBtn = container.querySelector('[data-clear-search]');
    if (clearSearchBtn) {
      clearSearchBtn.addEventListener('click', () => this.clearSearch());
    }

    // Filter dropdowns
    this.attachDropdownHandlers(container, 'filter');

    // Sort dropdown
    this.attachDropdownHandlers(container, 'sort');

    // Sort direction toggle
    const sortDirBtn = container.querySelector('[data-sort-direction]');
    if (sortDirBtn) {
      sortDirBtn.addEventListener('click', () => this.toggleSortDirection());
    }

    // Clear all filters
    const clearAllBtn = container.querySelector('[data-clear-all]');
    if (clearAllBtn) {
      clearAllBtn.addEventListener('click', () => this.clearAllFilters());
    }

    // Filter tag removal
    container.querySelectorAll('[data-remove-tag]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const action = e.currentTarget.getAttribute('data-remove-tag');
        this.handleTagRemoval(action);
      });
    });

    // Close dropdowns when clicking outside
    this.attachOutsideClickHandler(container);
  }

  /**
   * Attach dropdown handlers (filter or sort)
   */
  attachDropdownHandlers(container, type) {
    const triggers = container.querySelectorAll(`[data-${type}-trigger]`);

    triggers.forEach(trigger => {
      const id = type === 'filter' ? trigger.getAttribute(`data-${type}-trigger`) : null;
      const menu = id
        ? container.querySelector(`[data-${type}-menu="${id}"]`)
        : container.querySelector(`[data-${type}-menu]`);

      if (!menu) return;

      // Toggle dropdown
      trigger.addEventListener('click', (e) => {
        e.stopPropagation();
        const isOpen = menu.classList.contains('open');

        // Close all other dropdowns
        container.querySelectorAll('.filter-dropdown-menu.open, .sort-dropdown-menu.open').forEach(m => {
          if (m !== menu) {
            m.classList.remove('open');
            m.previousElementSibling?.setAttribute('aria-expanded', 'false');
          }
        });

        // Toggle this dropdown
        menu.classList.toggle('open', !isOpen);
        trigger.setAttribute('aria-expanded', !isOpen);
      });

      // Handle option selection
      const options = menu.querySelectorAll(`[data-${type}-option]`);
      options.forEach(option => {
        option.addEventListener('click', (e) => {
          e.stopPropagation();
          const value = option.getAttribute('data-value');

          if (type === 'filter') {
            this.setFilter(id, value);
          } else {
            this.setSort(value);
          }

          // Close dropdown
          menu.classList.remove('open');
          trigger.setAttribute('aria-expanded', 'false');
        });
      });
    });
  }

  /**
   * Attach outside click handler to close dropdowns
   */
  attachOutsideClickHandler(container) {
    document.addEventListener('click', (e) => {
      if (!container.contains(e.target)) {
        container.querySelectorAll('.filter-dropdown-menu.open, .sort-dropdown-menu.open').forEach(menu => {
          menu.classList.remove('open');
          menu.previousElementSibling?.setAttribute('aria-expanded', 'false');
        });
      }
    });
  }

  /**
   * Handle search input with debouncing
   */
  handleSearchInput(value) {
    this.state.searchQuery = value;

    // Clear previous timer
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }

    // Debounce search
    this.searchDebounceTimer = setTimeout(() => {
      this.emitFilterChange();
    }, this.debounceDelay);
  }

  /**
   * Clear search
   */
  clearSearch() {
    this.state.searchQuery = '';
    this.emitFilterChange();
  }

  /**
   * Set filter value
   */
  setFilter(filterId, value) {
    if (!value) {
      delete this.state.activeFilters[filterId];
    } else {
      this.state.activeFilters[filterId] = value;
    }
    this.emitFilterChange();
  }

  /**
   * Set sort option
   */
  setSort(value) {
    this.state.sortBy = value;
    this.emitFilterChange();
  }

  /**
   * Toggle sort direction
   */
  toggleSortDirection() {
    this.state.sortDirection = this.state.sortDirection === 'asc' ? 'desc' : 'asc';
    this.emitFilterChange();
  }

  /**
   * Clear all filters
   */
  clearAllFilters() {
    this.state.searchQuery = '';
    this.state.activeFilters = {};
    this.emitFilterChange();
  }

  /**
   * Handle tag removal
   */
  handleTagRemoval(action) {
    if (action === 'clear-search') {
      this.clearSearch();
    } else if (action.startsWith('clear-filter-')) {
      const filterId = action.replace('clear-filter-', '');
      this.setFilter(filterId, null);
    }
  }

  /**
   * Emit filter change event
   */
  emitFilterChange() {
    this.eventBus.emit('search-filter-changed', {
      search: this.state.searchQuery,
      filters: { ...this.state.activeFilters },
      sortBy: this.state.sortBy,
      sortDirection: this.state.sortDirection
    });
  }

  /**
   * Get current filter state
   */
  getState() {
    return {
      search: this.state.searchQuery,
      filters: { ...this.state.activeFilters },
      sortBy: this.state.sortBy,
      sortDirection: this.state.sortDirection
    };
  }

  /**
   * Set filter state programmatically
   */
  setState(newState) {
    if (newState.search !== undefined) {
      this.state.searchQuery = newState.search;
    }
    if (newState.filters) {
      this.state.activeFilters = { ...newState.filters };
    }
    if (newState.sortBy !== undefined) {
      this.state.sortBy = newState.sortBy;
    }
    if (newState.sortDirection !== undefined) {
      this.state.sortDirection = newState.sortDirection;
    }
    this.emitFilterChange();
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
