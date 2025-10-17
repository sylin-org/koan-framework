/**
 * Filters Component
 * Advanced photo filtering in right sidebar
 */

export class Filters {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.sidebar-right .panel');
    this.metadata = {
      cameraModels: [],
      years: [],
      tags: []
    };
    this.activeFilters = {
      camera: null,
      year: null,
      rating: null,
      tags: []
    };
  }

  async init() {
    // Load filter metadata
    await this.loadMetadata();
    // Render filter UI
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
      <h3>Filters</h3>

      ${this.renderCameraFilter()}
      ${this.renderYearFilter()}
      ${this.renderRatingFilter()}
      ${this.renderTagsFilter()}

      <button class="btn-reset-filters">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="1 4 1 10 7 10"></polyline>
          <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"></path>
        </svg>
        Reset Filters
      </button>
    `;

    this.attachListeners();
  }

  renderCameraFilter() {
    if (this.metadata.cameraModels.length === 0) {
      return '';
    }

    return `
      <div class="filter-section">
        <h4>Camera</h4>
        <select class="filter-select filter-camera">
          <option value="">All Cameras</option>
          ${this.metadata.cameraModels.map(camera => `
            <option value="${this.escapeHtml(camera)}">${this.escapeHtml(camera)}</option>
          `).join('')}
        </select>
      </div>
    `;
  }

  renderYearFilter() {
    if (this.metadata.years.length === 0) {
      return '';
    }

    return `
      <div class="filter-section">
        <h4>Year</h4>
        <select class="filter-select filter-year">
          <option value="">All Years</option>
          ${this.metadata.years.map(year => `
            <option value="${year}">${year}</option>
          `).join('')}
        </select>
      </div>
    `;
  }

  renderRatingFilter() {
    return `
      <div class="filter-section">
        <h4>Rating</h4>
        <div class="rating-filter">
          ${[5, 4, 3, 2, 1].map(rating => `
            <label class="rating-option">
              <input type="checkbox" value="${rating}" class="filter-rating" />
              <span class="rating-stars">
                ${this.renderStars(rating)}
              </span>
              <span class="rating-label">${rating}+ stars</span>
            </label>
          `).join('')}
        </div>
      </div>
    `;
  }

  renderStars(count) {
    let html = '';
    for (let i = 0; i < count; i++) {
      html += `
        <svg class="star-icon" width="14" height="14" viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-width="2">
          <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
        </svg>
      `;
    }
    return html;
  }

  renderTagsFilter() {
    if (this.metadata.tags.length === 0) {
      return '';
    }

    const topTags = this.metadata.tags.slice(0, 20); // Show top 20 tags

    return `
      <div class="filter-section">
        <h4>Tags</h4>
        <div class="tag-cloud">
          ${topTags.map(tag => `
            <button class="tag-chip" data-tag="${this.escapeHtml(tag.tag)}">
              ${this.escapeHtml(tag.tag)}
              <span class="tag-count">${tag.count}</span>
            </button>
          `).join('')}
        </div>
      </div>
    `;
  }

  attachListeners() {
    // Camera filter
    const cameraSelect = this.container.querySelector('.filter-camera');
    if (cameraSelect) {
      cameraSelect.addEventListener('change', (e) => {
        this.activeFilters.camera = e.target.value || null;
        this.applyFilters();
      });
    }

    // Year filter
    const yearSelect = this.container.querySelector('.filter-year');
    if (yearSelect) {
      yearSelect.addEventListener('change', (e) => {
        this.activeFilters.year = e.target.value ? parseInt(e.target.value) : null;
        this.applyFilters();
      });
    }

    // Rating filter (checkboxes)
    const ratingCheckboxes = this.container.querySelectorAll('.filter-rating');
    ratingCheckboxes.forEach(checkbox => {
      checkbox.addEventListener('change', () => {
        const checkedRatings = Array.from(this.container.querySelectorAll('.filter-rating:checked'))
          .map(cb => parseInt(cb.value));

        this.activeFilters.rating = checkedRatings.length > 0 ? Math.min(...checkedRatings) : null;
        this.applyFilters();
      });
    });

    // Tag chips
    const tagChips = this.container.querySelectorAll('.tag-chip');
    tagChips.forEach(chip => {
      chip.addEventListener('click', () => {
        const tag = chip.dataset.tag;
        chip.classList.toggle('active');

        if (chip.classList.contains('active')) {
          this.activeFilters.tags.push(tag);
        } else {
          this.activeFilters.tags = this.activeFilters.tags.filter(t => t !== tag);
        }

        this.applyFilters();
      });
    });

    // Reset button
    const resetBtn = this.container.querySelector('.btn-reset-filters');
    if (resetBtn) {
      resetBtn.addEventListener('click', () => {
        this.resetFilters();
      });
    }
  }

  applyFilters() {
    let filtered = [...this.app.state.photos];

    // Apply camera filter
    if (this.activeFilters.camera) {
      filtered = filtered.filter(p => p.cameraModel === this.activeFilters.camera);
    }

    // Apply year filter
    if (this.activeFilters.year) {
      filtered = filtered.filter(p => {
        if (!p.capturedAt) return false;
        const year = new Date(p.capturedAt).getFullYear();
        return year === this.activeFilters.year;
      });
    }

    // Apply rating filter (minimum rating)
    if (this.activeFilters.rating !== null) {
      filtered = filtered.filter(p => (p.rating || 0) >= this.activeFilters.rating);
    }

    // Apply tag filters (photos must have ALL selected tags)
    if (this.activeFilters.tags.length > 0) {
      filtered = filtered.filter(p => {
        return this.activeFilters.tags.every(tag => p.autoTags && p.autoTags.includes(tag));
      });
    }

    // Update the grid with filtered photos
    const originalPhotos = this.app.state.photos;
    this.app.state.photos = filtered;
    this.app.components.grid.render();

    // Show toast with filter results
    const activeFilterCount = [
      this.activeFilters.camera,
      this.activeFilters.year,
      this.activeFilters.rating,
      ...this.activeFilters.tags
    ].filter(Boolean).length;

    if (activeFilterCount > 0) {
      this.app.components.toast.show(
        `Showing ${filtered.length} of ${originalPhotos.length} photos (${activeFilterCount} filter${activeFilterCount > 1 ? 's' : ''})`,
        { icon: '🔍', duration: 2000 }
      );
    }
  }

  resetFilters() {
    // Clear all active filters
    this.activeFilters = {
      camera: null,
      year: null,
      rating: null,
      tags: []
    };

    // Reset UI
    const cameraSelect = this.container.querySelector('.filter-camera');
    if (cameraSelect) cameraSelect.value = '';

    const yearSelect = this.container.querySelector('.filter-year');
    if (yearSelect) yearSelect.value = '';

    const ratingCheckboxes = this.container.querySelectorAll('.filter-rating');
    ratingCheckboxes.forEach(cb => cb.checked = false);

    const tagChips = this.container.querySelectorAll('.tag-chip');
    tagChips.forEach(chip => chip.classList.remove('active'));

    // Reload all photos
    this.app.loadPhotos();

    this.app.components.toast.show('Filters reset', { icon: '🔄', duration: 1500 });
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
