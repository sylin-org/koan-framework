/**
 * SearchBar - Top search with space selector
 */

import { Events } from '../utils/EventBus.js';
import { escapeHtml, escapeAttr } from '../utils/html.js';

export class SearchBar {
    constructor(app) {
        this.app = app;
        this.container = document.getElementById('search-bar');
        this.debounceTimer = null;

        this.app.events.on(Events.SPACES_LOADED, () => this.updateSpaceOptions());

        // U18: Sync space dropdown with sidebar selection
        this.app.events.on(Events.SPACE_SELECTED, (spaceId) => {
            const spaceSelect = this.container.querySelector('#search-space-select');
            if (spaceSelect) {
                spaceSelect.value = spaceId || '';
            }
        });

        this.render();
    }

    render() {
        const spaces = this.app.state.get('spaces') || [];
        const currentSpace = this.app.state.get('currentSpace');

        // U5: Lens dropdown removed — non-functional UI should not ship
        this.container.innerHTML = `
            <div class="search-container">
                <div class="search-input-wrap">
                    <span class="search-icon">&#128269;</span>
                    <input type="text"
                           class="search-input"
                           id="search-input"
                           placeholder="Search notes, concepts, sources..."
                           aria-label="Search notes"
                           autocomplete="off">
                </div>
                <select class="search-select" id="search-space-select" title="Filter by space">
                    <option value="">All spaces</option>
                    ${spaces.map(s => `
                        <option value="${escapeAttr(s.id)}" ${s.id === currentSpace ? 'selected' : ''}>
                            ${escapeHtml(s.name)}
                        </option>
                    `).join('')}
                </select>
            </div>
        `;

        this.bindEvents();
    }

    bindEvents() {
        const input = this.container.querySelector('#search-input');
        const spaceSelect = this.container.querySelector('#search-space-select');

        // Debounced search
        input.addEventListener('input', () => {
            clearTimeout(this.debounceTimer);
            this.debounceTimer = setTimeout(() => {
                this.performSearch(input.value.trim());
            }, 300);
        });

        // Clear search on Escape
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                input.value = '';
                this.clearSearch();
            }
        });

        // Space filter change triggers re-search
        spaceSelect.addEventListener('change', () => {
            const query = input.value.trim();
            if (query) {
                this.performSearch(query);
            }
        });
    }

    async performSearch(query) {
        if (!query) {
            this.clearSearch();
            return;
        }

        this.app.state.set('currentView', 'search');
        this.app.events.emit(Events.VIEW_CHANGED, 'search');
        this.app.events.emit(Events.STATUS_UPDATE, 'Searching...');

        try {
            const spaceSelect = this.container.querySelector('#search-space-select');
            const selectedSpace = spaceSelect.value;
            const spaceIds = selectedSpace ? [selectedSpace] : undefined;

            const result = await this.app.api.post('/api/search', {
                query,
                spaceIds,
                maxResults: 50
            });

            this.app.events.emit(Events.SEARCH_RESULTS, {
                query: result.query,
                count: result.count,
                results: result.results
            });

            this.app.events.emit(Events.STATUS_UPDATE,
                `Found ${result.count} result${result.count !== 1 ? 's' : ''} for "${query}"`);
        } catch (error) {
            console.error('[SearchBar] Search failed:', error);
            this.app.showToast('Search failed', 'error');
            this.app.events.emit(Events.SEARCH_RESULTS, {
                query,
                count: 0,
                results: [],
                error: error.message
            });
            this.app.events.emit(Events.STATUS_UPDATE, 'Search failed');
        }
    }

    clearSearch() {
        this.app.state.set('currentView', 'pulse');
        this.app.events.emit(Events.SEARCH_CLEAR);
        this.app.events.emit(Events.VIEW_CHANGED, 'pulse');
        this.app.events.emit(Events.STATUS_UPDATE, 'Ready');
    }

    updateSpaceOptions() {
        // Re-render to pick up new spaces
        this.render();
    }
}
