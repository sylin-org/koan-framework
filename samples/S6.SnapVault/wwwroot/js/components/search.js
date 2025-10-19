/**
 * Search Bar Component with Semantic/Exact Slider
 * Industry-first hybrid search control
 */

export class SearchBar {
  constructor(app) {
    this.app = app;
    this.container = document.querySelector('.search-container');
    this.alpha = 0.5; // 50% semantic, 50% exact (hybrid default)
    this.realtime = true;
    this.debounceTimer = null;
    this.previousViewState = null; // Track view before search
    this.render();
  }

  render() {
    if (!this.container) return;

    this.container.innerHTML = `
      <div class="search-pro">
        <div class="search-input-wrapper">
          <svg class="search-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"></circle>
            <path d="m21 21-4.35-4.35"></path>
          </svg>
          <input
            type="search"
            class="search-input"
            placeholder="Search photos..."
            aria-label="Search photos"
          />
          <kbd class="search-hint">/</kbd>
        </div>

        <div class="search-mode-control">
          <label class="search-label">Search Mode</label>
          <div class="slider-container">
            <input
              type="range"
              class="search-slider"
              min="0"
              max="100"
              value="50"
              step="5"
              aria-label="Semantic to exact search balance"
            />
            <div class="slider-track">
              <div class="slider-fill" style="width: 50%;"></div>
            </div>
            <div class="slider-labels">
              <span class="label-left">Exact</span>
              <span class="label-center">Hybrid</span>
              <span class="label-right">Semantic</span>
            </div>
          </div>
          <output class="search-mode-output">Hybrid (50%)</output>
        </div>

        <label class="realtime-toggle">
          <input type="checkbox" checked aria-label="Real-time search" />
          <span>Real-time</span>
          <kbd>⌃R</kbd>
        </label>
      </div>
    `;

    this.setupEventListeners();
  }

  setupEventListeners() {
    const input = this.container.querySelector('.search-input');
    const slider = this.container.querySelector('.search-slider');
    const realtimeToggle = this.container.querySelector('.realtime-toggle input');

    // Search input
    input.addEventListener('input', (e) => {
      if (this.realtime) {
        this.debounceSearch(e.target.value);
      }
    });

    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        this.performSearch(e.target.value);
      }
      if (e.key === 'Escape') {
        input.value = '';
        input.blur();
        this.clearSearch();
      }
    });

    // Semantic/exact slider
    slider.addEventListener('input', (e) => {
      const value = parseInt(e.target.value);
      this.alpha = value / 100;
      this.updateSliderUI(value);

      if (this.realtime && input.value) {
        this.debounceSearch(input.value);
      }
    });

    // Real-time toggle
    realtimeToggle.addEventListener('change', (e) => {
      this.realtime = e.target.checked;
    });
  }

  updateSliderUI(value) {
    const fill = this.container.querySelector('.slider-fill');
    const output = this.container.querySelector('.search-mode-output');

    fill.style.width = `${value}%`;

    let mode = 'Hybrid';
    if (value < 30) mode = 'Exact';
    else if (value > 70) mode = 'Semantic';

    output.textContent = `${mode} (${value}%)`;

    // Update slider gradient color
    const slider = this.container.querySelector('.search-slider');
    slider.style.setProperty('--slider-value', value);
  }

  debounceSearch(query) {
    clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      this.performSearch(query);
    }, 300); // 300ms debounce
  }

  async performSearch(query) {
    if (!query.trim()) {
      this.clearSearch();
      return;
    }

    try {
      // Save current view state before first search
      if (!this.previousViewState) {
        this.previousViewState = { ...this.app.components.collectionView.viewState };
      }

      const response = await this.app.api.post('/api/photos/search', {
        query: query,
        alpha: this.alpha,
        limit: 100
      });

      this.app.state.photos = response.photos || [];
      this.app.components.grid.render();

      this.app.components.toast.show(`Found ${response.resultCount || 0} photos`, {
        icon: '🔍',
        duration: 2000
      });
    } catch (error) {
      console.error('Search failed:', error);
      this.app.components.toast.show('Search failed', {
        icon: '⚠️',
        duration: 3000
      });
    }
  }

  clearSearch() {
    // Restore previous view state if we have it
    if (this.previousViewState) {
      const { type } = this.previousViewState;

      if (type === 'all-photos') {
        this.app.components.collectionView.setView('all-photos');
      } else if (type === 'favorites') {
        this.app.components.collectionView.setView('favorites');
      } else if (type === 'collection') {
        this.app.components.collectionView.setView(this.previousViewState.collection.id);
      }

      this.previousViewState = null; // Clear saved state
    } else {
      // No previous state, default to all photos
      this.app.components.collectionView.setView('all-photos');
    }
  }

  focus() {
    const input = this.container?.querySelector('.search-input');
    if (input) {
      input.focus();
      input.select();
    }
  }
}
