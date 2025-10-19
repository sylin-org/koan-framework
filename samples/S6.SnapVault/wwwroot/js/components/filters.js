/**
 * Filters Component - Design System Compliant
 *
 * Advanced photo filtering using EntityController's JSON filter system.
 * Built with SnapVault Pro Design System components.
 *
 * See /DESIGN_SYSTEM.md for component documentation.
 */


import { escapeHtml } from '../utils/html.js';
export class Filters {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.sidebar-right .panel');
    this.metadata = {
      cameraModels: [],
      years: [],
      tags: []
    };
    this.state = {
      cameras: [],
      dateRange: null,
      rating: null,
      includeUnrated: false,
      tags: [],
      tagMatchMode: 'all',
      favorites: false,
      eventId: null
    };
    this.previewDebounce = null;
  }

  async init() {
    await this.loadMetadata();
    this.render();
  }

  async loadMetadata() {
    try {
      const response = await this.app.api.get('/api/photos/filter-metadata');
      this.metadata = {
        cameraModels: response.cameraModels || [],
        years: response.years || [],
        tags: response.tags || []
      };
    } catch (error) {
      console.error('Failed to load filter metadata:', error);
    }
  }

  render() {
    if (!this.container) return;

    this.container.innerHTML = `
      <h3 class="panel-title">Filters</h3>

      <!-- Active Filters Summary -->
      <div class="active-filters-container"></div>

      <!-- Camera Section -->
      ${this.renderCameraSection()}

      <!-- Date Range Section -->
      ${this.renderDateSection()}

      <!-- Rating Section -->
      ${this.renderRatingSection()}

      <!-- Tags Section -->
      ${this.renderTagsSection()}

      <!-- Quick Filters Section -->
      ${this.renderQuickFiltersSection()}

      <!-- Actions -->
      <button class="btn btn-ghost w-full mt-3">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="1 4 1 10 7 10"></polyline>
          <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"></path>
        </svg>
        Reset Filters
      </button>

      <!-- Result Count -->
      <div class="result-count-container"></div>
    `;

    this.attachListeners();
    this.renderActiveFilterPills();
  }

  renderCameraSection() {
    if (this.metadata.cameraModels.length === 0) return '';

    return `
      <div class="section">
        <h4 class="section-header">CAMERA</h4>
        <div class="section-content">
          <input
            type="search"
            class="form-input form-input-search mb-2"
            placeholder="Search cameras..."
          />
          <div class="camera-list overflow-y-auto max-h-200">
            ${this.metadata.cameraModels.map(camera => `
              <label class="checkbox-label">
                <input
                  type="checkbox"
                  class="checkbox camera-checkbox"
                  value="${escapeHtml(camera)}"
                  ${this.state.cameras.includes(camera) ? 'checked' : ''}
                />
                <span class="checkbox-text">${escapeHtml(camera)}</span>
              </label>
            `).join('')}
          </div>
        </div>
      </div>
    `;
  }

  renderDateSection() {
    const today = new Date().toISOString().split('T')[0];

    return `
      <div class="section">
        <h4 class="section-header">DATE CAPTURED</h4>
        <div class="section-content">
          <!-- Quick Presets -->
          <div class="preset-group mb-2">
            <button class="preset-btn" data-preset="today">Today</button>
            <button class="preset-btn" data-preset="week">Week</button>
            <button class="preset-btn" data-preset="month">Month</button>
            <button class="preset-btn" data-preset="year">Year</button>
          </div>

          <!-- Custom Range -->
          <div class="flex flex-col gap-2">
            <label class="text-xs text-tertiary uppercase" style="letter-spacing: 0.05em;">
              Custom Range
            </label>
            <div class="flex flex-col gap-2">
              <input
                type="date"
                class="form-input date-from"
                value="${this.state.dateRange?.from.split('T')[0] || ''}"
              />
              <div class="text-xs text-tertiary text-center">to</div>
              <input
                type="date"
                class="form-input date-to"
                value="${this.state.dateRange?.to.split('T')[0] || today}"
              />
            </div>
          </div>
        </div>
      </div>
    `;
  }

  renderRatingSection() {
    return `
      <div class="section">
        <h4 class="section-header">RATING</h4>
        <div class="section-content">
          <div class="slider-container">
            <div class="slider-value mb-2">
              <span class="slider-value-stars">${this.renderStarsDisplay(this.state.rating || 0)}</span>
              <span class="text-sm text-secondary">${this.state.rating ? `${this.state.rating}+ stars` : 'All ratings'}</span>
            </div>
            <input
              type="range"
              min="0"
              max="5"
              step="1"
              class="slider rating-slider"
              value="${this.state.rating || 0}"
            />
          </div>

          <label class="checkbox-label mt-2">
            <input
              type="checkbox"
              class="checkbox include-unrated"
              ${this.state.includeUnrated ? 'checked' : ''}
            />
            <span class="checkbox-text">Include unrated</span>
          </label>
        </div>
      </div>
    `;
  }

  renderTagsSection() {
    if (this.metadata.tags.length === 0) return '';

    const topTags = this.metadata.tags.slice(0, 30);

    return `
      <div class="section">
        <h4 class="section-header">TAGS</h4>
        <div class="section-content">
          <!-- Search -->
          <input
            type="search"
            class="form-input form-input-search mb-2"
            placeholder="Search tags..."
          />

          <!-- Match Mode -->
          <div class="radio-group mb-2">
            <label class="radio-label">
              <input
                type="radio"
                name="tag-match"
                value="all"
                class="radio"
                ${this.state.tagMatchMode === 'all' ? 'checked' : ''}
              />
              <span>All tags</span>
            </label>
            <label class="radio-label">
              <input
                type="radio"
                name="tag-match"
                value="any"
                class="radio"
                ${this.state.tagMatchMode === 'any' ? 'checked' : ''}
              />
              <span>Any tag</span>
            </label>
          </div>

          <!-- Tag Cloud -->
          <div class="tag-cloud">
            ${topTags.map(tag => `
              <button
                class="tag-pill ${this.state.tags.includes(tag.tag) ? 'active' : ''}"
                data-tag="${escapeHtml(tag.tag)}"
              >
                ${escapeHtml(tag.tag)}
                <span class="tag-pill-count">${tag.count}</span>
              </button>
            `).join('')}
          </div>

          ${this.metadata.tags.length > 30 ? `
            <button class="btn btn-ghost w-full mt-2 text-xs">
              Show all ${this.metadata.tags.length} tags
            </button>
          ` : ''}
        </div>
      </div>
    `;
  }

  renderQuickFiltersSection() {
    return `
      <div class="section">
        <h4 class="section-header">QUICK FILTERS</h4>
        <div class="section-content">
          <label class="checkbox-label">
            <input
              type="checkbox"
              class="checkbox favorites-only"
              ${this.state.favorites ? 'checked' : ''}
            />
            <span class="checkbox-text">⭐ Favorites only</span>
          </label>
        </div>
      </div>
    `;
  }

  renderStarsDisplay(count) {
    if (count === 0) return '☆☆☆☆☆';
    let html = '';
    for (let i = 0; i < 5; i++) {
      html += i < count ? '★' : '☆';
    }
    return html;
  }

  attachListeners() {
    // Camera checkboxes
    const cameraCheckboxes = this.container.querySelectorAll('.camera-checkbox');
    cameraCheckboxes.forEach(checkbox => {
      checkbox.addEventListener('change', (e) => {
        const camera = e.target.value;
        if (e.target.checked) {
          if (!this.state.cameras.includes(camera)) {
            this.state.cameras.push(camera);
          }
        } else {
          this.state.cameras = this.state.cameras.filter(c => c !== camera);
        }
        this.applyFilters();
      });
    });

    // Camera search
    const cameraSearch = this.container.querySelector('.form-input-search');
    if (cameraSearch) {
      cameraSearch.addEventListener('input', (e) => {
        this.filterCameraList(e.target.value);
      });
    }

    // Date presets
    const presetBtns = this.container.querySelectorAll('.preset-btn');
    presetBtns.forEach(btn => {
      btn.addEventListener('click', () => {
        const preset = btn.dataset.preset;
        this.applyDatePreset(preset);

        // Visual feedback
        presetBtns.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
      });
    });

    // Date range inputs
    const dateFrom = this.container.querySelector('.date-from');
    const dateTo = this.container.querySelector('.date-to');

    if (dateFrom) {
      dateFrom.addEventListener('change', (e) => {
        if (e.target.value) {
          this.state.dateRange = this.state.dateRange || {};
          this.state.dateRange.from = new Date(e.target.value).toISOString();
          this.applyFilters();
        } else {
          this.state.dateRange = null;
          this.applyFilters();
        }
      });
    }

    if (dateTo) {
      dateTo.addEventListener('change', (e) => {
        if (e.target.value) {
          this.state.dateRange = this.state.dateRange || {};
          this.state.dateRange.to = new Date(e.target.value + 'T23:59:59').toISOString();
          this.applyFilters();
        }
      });
    }

    // Rating slider
    const ratingSlider = this.container.querySelector('.rating-slider');
    if (ratingSlider) {
      ratingSlider.addEventListener('input', (e) => {
        const value = parseInt(e.target.value);
        this.state.rating = value > 0 ? value : null;
        this.updateRatingDisplay(value);
        this.previewResultCount();
      });

      ratingSlider.addEventListener('change', () => {
        this.applyFilters();
      });
    }

    // Include unrated checkbox
    const includeUnrated = this.container.querySelector('.include-unrated');
    if (includeUnrated) {
      includeUnrated.addEventListener('change', (e) => {
        this.state.includeUnrated = e.target.checked;
        this.applyFilters();
      });
    }

    // Tag match mode
    const tagMatchRadios = this.container.querySelectorAll('input[name="tag-match"]');
    tagMatchRadios.forEach(radio => {
      radio.addEventListener('change', (e) => {
        this.state.tagMatchMode = e.target.value;
        if (this.state.tags.length > 0) {
          this.applyFilters();
        }
      });
    });

    // Tag chips
    const tagChips = this.container.querySelectorAll('.tag-pill');
    tagChips.forEach(chip => {
      chip.addEventListener('click', () => {
        const tag = chip.dataset.tag;
        chip.classList.toggle('active');

        if (chip.classList.contains('active')) {
          if (!this.state.tags.includes(tag)) {
            this.state.tags.push(tag);
          }
        } else {
          this.state.tags = this.state.tags.filter(t => t !== tag);
        }

        this.applyFilters();
      });
    });

    // Favorites checkbox
    const favoritesOnly = this.container.querySelector('.favorites-only');
    if (favoritesOnly) {
      favoritesOnly.addEventListener('change', (e) => {
        this.state.favorites = e.target.checked;
        this.applyFilters();
      });
    }

    // Reset button
    const resetBtn = this.container.querySelector('.btn-ghost');
    if (resetBtn) {
      resetBtn.addEventListener('click', () => {
        this.resetFilters();
      });
    }
  }

  applyDatePreset(preset) {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    let from, to;

    switch (preset) {
      case 'today':
        from = today;
        to = new Date(today.getTime() + 24 * 60 * 60 * 1000 - 1);
        break;
      case 'week':
        from = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
        to = now;
        break;
      case 'month':
        from = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
        to = now;
        break;
      case 'year':
        from = new Date(now.getFullYear(), 0, 1);
        to = now;
        break;
    }

    this.state.dateRange = {
      from: from.toISOString(),
      to: to.toISOString()
    };

    // Update date inputs
    const dateFrom = this.container.querySelector('.date-from');
    const dateTo = this.container.querySelector('.date-to');
    if (dateFrom) dateFrom.value = from.toISOString().split('T')[0];
    if (dateTo) dateTo.value = to.toISOString().split('T')[0];

    this.applyFilters();
  }

  updateRatingDisplay(value) {
    const starsDisplay = this.container.querySelector('.slider-value-stars');
    const label = this.container.querySelector('.slider-value .text-sm');

    if (starsDisplay) {
      starsDisplay.textContent = this.renderStarsDisplay(value);
    }

    if (label) {
      label.textContent = value > 0 ? `${value}+ stars` : 'All ratings';
    }
  }

  filterCameraList(searchTerm) {
    const cameraList = this.container.querySelector('.camera-list');
    const labels = cameraList.querySelectorAll('.checkbox-label');
    const term = searchTerm.toLowerCase();

    labels.forEach(label => {
      const text = label.textContent.toLowerCase();
      label.style.display = text.includes(term) ? '' : 'none';
    });
  }

  buildFilterJson() {
    const conditions = [];

    // Camera filter
    if (this.state.cameras.length > 0) {
      conditions.push({
        CameraModel: { $in: this.state.cameras }
      });
    }

    // Date range filter
    if (this.state.dateRange) {
      conditions.push({
        CapturedAt: {
          $gte: this.state.dateRange.from,
          $lte: this.state.dateRange.to
        }
      });
    }

    // Rating filter
    if (this.state.rating !== null) {
      const ratings = [];
      for (let i = this.state.rating; i <= 5; i++) {
        ratings.push(i);
      }

      if (this.state.includeUnrated) {
        conditions.push({
          $or: [
            { Rating: { $in: ratings } },
            { Rating: { $in: [0, null] } }
          ]
        });
      } else {
        conditions.push({
          Rating: { $in: ratings }
        });
      }
    }

    // Tag filter
    if (this.state.tags.length > 0) {
      if (this.state.tagMatchMode === 'all') {
        conditions.push({
          AutoTags: { $all: this.state.tags }
        });
      } else {
        const tagConditions = this.state.tags.map(tag => ({
          AutoTags: tag
        }));

        if (tagConditions.length > 1) {
          conditions.push({ $or: tagConditions });
        } else {
          conditions.push(tagConditions[0]);
        }
      }
    }

    // Favorites filter
    if (this.state.favorites) {
      conditions.push({ IsFavorite: true });
    }

    // Event filter
    if (this.state.eventId) {
      conditions.push({ EventId: this.state.eventId });
    }

    // Combine conditions
    if (conditions.length === 0) {
      return null;
    } else if (conditions.length === 1) {
      return conditions[0];
    } else {
      return { $and: conditions };
    }
  }

  async applyFilters() {
    const filterJson = this.buildFilterJson();

    try {
      const params = {
        page: 1,
        pageSize: 200,
        sort: '-CapturedAt'
      };

      if (filterJson) {
        params.filter = JSON.stringify(filterJson);
      }

      const result = await this.app.api.get('/api/photos', params, { includeHeaders: true });

      this.app.state.photos = result.data || [];
      this.app.state.totalCount = result.headers.totalCount || 0;
      this.app.state.currentPage = 1;

      this.renderActiveFilterPills();
      this.renderResultCount(result.headers.totalCount);
      this.app.components.grid.render();

      // Show toast notification
      const filterCount = this.getActiveFilterCount();
      if (filterCount > 0) {
        this.app.components.toast.show(
          `Found ${result.headers.totalCount} photos (${filterCount} filter${filterCount > 1 ? 's' : ''})`,
          { icon: '🔍', duration: 2000 }
        );
      }

    } catch (error) {
      console.error('Filter failed:', error);
      this.app.components.toast.show(
        'Filter failed: ' + error.message,
        { icon: '⚠️', duration: 3000 }
      );
    }
  }

  async previewResultCount() {
    clearTimeout(this.previewDebounce);
    this.previewDebounce = setTimeout(async () => {
      const filterJson = this.buildFilterJson();

      if (!filterJson) return;

      try {
        const result = await this.app.api.get('/api/photos', {
          filter: JSON.stringify(filterJson),
          page: 1,
          pageSize: 1
        }, { includeHeaders: true });

        this.renderResultCount(result.headers.totalCount);
      } catch (error) {
        console.error('Preview count failed:', error);
      }
    }, 300);
  }

  renderActiveFilterPills() {
    const container = this.container.querySelector('.active-filters-container');
    if (!container) return;

    const pills = [];

    // Camera pills
    this.state.cameras.forEach(camera => {
      pills.push({ label: camera, type: 'camera', value: camera });
    });

    // Date range pill
    if (this.state.dateRange) {
      const from = new Date(this.state.dateRange.from).toLocaleDateString();
      const to = new Date(this.state.dateRange.to).toLocaleDateString();
      pills.push({ label: `${from} - ${to}`, type: 'date', value: null });
    }

    // Rating pill
    if (this.state.rating !== null) {
      pills.push({ label: `${this.state.rating}+ stars`, type: 'rating', value: null });
    }

    // Tag pills
    this.state.tags.forEach(tag => {
      pills.push({ label: tag, type: 'tag', value: tag });
    });

    // Favorites pill
    if (this.state.favorites) {
      pills.push({ label: 'Favorites', type: 'favorites', value: null });
    }

    if (pills.length === 0) {
      container.innerHTML = '';
      return;
    }

    container.innerHTML = `
      <div class="active-filters-summary">
        <span class="filter-count">ACTIVE (${pills.length})</span>
        ${pills.map(pill => `
          <span class="filter-pill">
            ${escapeHtml(pill.label)}
            <button class="filter-pill-remove" data-type="${pill.type}" data-value="${pill.value || ''}">×</button>
          </span>
        `).join('')}
        <button class="clear-all-btn">Clear all</button>
      </div>
    `;

    // Attach remove listeners
    const removeBtns = container.querySelectorAll('.filter-pill-remove');
    removeBtns.forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const type = btn.dataset.type;
        const value = btn.dataset.value;
        this.removeFilter(type, value);
      });
    });

    // Attach clear all listener
    const clearAllBtn = container.querySelector('.clear-all-btn');
    if (clearAllBtn) {
      clearAllBtn.addEventListener('click', () => {
        this.resetFilters();
      });
    }
  }

  renderResultCount(count) {
    const container = this.container.querySelector('.result-count-container');
    if (!container) return;

    const total = this.app.state.totalCount || count;

    container.innerHTML = `
      <div class="result-count">
        Showing <strong>${count}</strong> of <strong>${total}</strong> photos
      </div>
    `;
  }

  removeFilter(type, value) {
    switch (type) {
      case 'camera':
        this.state.cameras = this.state.cameras.filter(c => c !== value);
        const cameraCheckbox = this.container.querySelector(`.camera-checkbox[value="${value}"]`);
        if (cameraCheckbox) cameraCheckbox.checked = false;
        break;
      case 'tag':
        this.state.tags = this.state.tags.filter(t => t !== value);
        const tagChip = this.container.querySelector(`.tag-pill[data-tag="${value}"]`);
        if (tagChip) tagChip.classList.remove('active');
        break;
      case 'date':
        this.state.dateRange = null;
        const dateFrom = this.container.querySelector('.date-from');
        const dateTo = this.container.querySelector('.date-to');
        if (dateFrom) dateFrom.value = '';
        if (dateTo) dateTo.value = '';
        break;
      case 'rating':
        this.state.rating = null;
        const ratingSlider = this.container.querySelector('.rating-slider');
        if (ratingSlider) {
          ratingSlider.value = 0;
          this.updateRatingDisplay(0);
        }
        break;
      case 'favorites':
        this.state.favorites = false;
        const favoritesCheckbox = this.container.querySelector('.favorites-only');
        if (favoritesCheckbox) favoritesCheckbox.checked = false;
        break;
    }

    this.applyFilters();
  }

  resetFilters() {
    // Clear state
    this.state = {
      cameras: [],
      dateRange: null,
      rating: null,
      includeUnrated: false,
      tags: [],
      tagMatchMode: 'all',
      favorites: false,
      eventId: null
    };

    // Reload all photos
    this.app.loadPhotos();

    this.app.components.toast.show('Filters reset', { icon: '🔄', duration: 1500 });
  }

  getActiveFilterCount() {
    let count = 0;
    if (this.state.cameras.length > 0) count += this.state.cameras.length;
    if (this.state.dateRange) count++;
    if (this.state.rating !== null) count++;
    if (this.state.tags.length > 0) count += this.state.tags.length;
    if (this.state.favorites) count++;
    return count;
  }

}
