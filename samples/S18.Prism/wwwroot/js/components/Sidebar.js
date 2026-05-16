/**
 * Sidebar - Left panel with Spaces, Sources, Research Briefs
 */

import { Events } from '../utils/EventBus.js';
import { escapeHtml, escapeAttr } from '../utils/html.js';

export class Sidebar {
    constructor(app) {
        this.app = app;
        this.container = document.getElementById('sidebar');

        this.app.events.on(Events.SPACES_LOADED, () => this.render());
        this.app.events.on(Events.SPACE_SELECTED, () => this.render());
        this.app.events.on(Events.SPACE_CREATED, () => this.loadAndRender());

        this.render();
    }

    render() {
        const spaces = this.app.state.get('spaces') || [];
        const currentSpaceId = this.app.state.get('currentSpace');
        const sources = this.app.state.get('sources') || [];
        const briefs = this.app.state.get('briefs') || [];

        // Count sources/briefs for current space
        const spaceSources = sources.filter(s => s.spaceId === currentSpaceId);
        const spaceBriefs = briefs.filter(b => b.spaceId === currentSpaceId);

        this.container.innerHTML = `
            <button class="sidebar-notes-btn" id="btn-view-notes" title="Browse all notes in this space">
                <span>&#128196;</span>
                <span>Notes</span>
            </button>

            <div class="sidebar-section">
                <div class="sidebar-heading">
                    <span>Spaces</span>
                    <button class="btn btn-ghost btn-sm" id="btn-add-space" title="New space">+</button>
                </div>
                <div class="sidebar-items" id="spaces-list">
                    ${spaces.map(space => `
                        <button class="sidebar-item ${space.id === currentSpaceId ? 'active' : ''}"
                                data-space-id="${escapeAttr(space.id)}">
                            <span class="space-dot" style="background: ${this.spaceColor(space.name)}"></span>
                            <span class="sidebar-item-label">${escapeHtml(space.name)}</span>
                        </button>
                    `).join('')}
                </div>
            </div>

            <div class="sidebar-section">
                <div class="sidebar-heading">
                    <span>Sources</span>
                    <span class="sidebar-item-count">${spaceSources.length}</span>
                </div>
                ${spaceSources.length === 0
                    ? '<div class="text-xs text-tertiary px-3 py-2">No sources configured</div>'
                    : spaceSources.map(source => `
                        <button class="sidebar-item" data-source-id="${escapeAttr(source.id)}">
                            <span class="sidebar-item-icon">${this.sourceIcon(source.type)}</span>
                            <span class="sidebar-item-label">${escapeHtml(source.name)}</span>
                            <span class="sidebar-item-count">${source.totalItemsPulled || 0}</span>
                        </button>
                    `).join('')}
            </div>

            <div class="sidebar-section">
                <div class="sidebar-heading">
                    <span>Research Briefs</span>
                    <span class="sidebar-item-count">${spaceBriefs.length}</span>
                </div>
                ${spaceBriefs.length === 0
                    ? '<div class="text-xs text-tertiary px-3 py-2">No research briefs</div>'
                    : spaceBriefs.map(brief => `
                        <button class="sidebar-item" data-brief-id="${escapeAttr(brief.id)}">
                            <span class="sidebar-item-icon" style="color: var(--accent-brief)">&#9671;</span>
                            <span class="sidebar-item-label">${escapeHtml(brief.name)}</span>
                            <span class="sidebar-item-count">${brief.totalItemsFound || 0}</span>
                        </button>
                    `).join('')}
            </div>
        `;

        this.bindEvents();
    }

    bindEvents() {
        // U1: Notes button
        const notesBtn = this.container.querySelector('#btn-view-notes');
        if (notesBtn) {
            notesBtn.addEventListener('click', () => {
                this.app.events.emit(Events.VIEW_NOTES);
            });
        }

        // Space selection
        this.container.querySelectorAll('[data-space-id]').forEach(el => {
            el.addEventListener('click', () => {
                const spaceId = el.dataset.spaceId;
                this.app.state.set('currentSpace', spaceId);
                this.app.state.set('currentView', 'pulse');
                this.app.events.emit(Events.SPACE_SELECTED, spaceId);
            });
        });

        // U2: Add space button — inline input instead of prompt()
        const addBtn = this.container.querySelector('#btn-add-space');
        if (addBtn) {
            addBtn.addEventListener('click', () => {
                const list = this.container.querySelector('#spaces-list');
                if (!list || list.querySelector('.space-name-input')) return;

                const input = document.createElement('input');
                input.type = 'text';
                input.className = 'space-name-input';
                input.placeholder = 'Space name...';
                input.addEventListener('keydown', async (e) => {
                    if (e.key === 'Enter' && input.value.trim()) {
                        await this.createSpace(input.value.trim());
                        input.remove();
                    }
                    if (e.key === 'Escape') input.remove();
                });
                input.addEventListener('blur', () => setTimeout(() => input.remove(), 200));
                list.prepend(input);
                input.focus();
            });
        }

        // U7: Source item click handlers
        this.container.querySelectorAll('[data-source-id]').forEach(el => {
            el.addEventListener('click', () => {
                this.app.events.emit(Events.SOURCE_SELECTED, el.dataset.sourceId);
            });
        });

        // U7: Brief item click handlers
        this.container.querySelectorAll('[data-brief-id]').forEach(el => {
            el.addEventListener('click', () => {
                this.app.events.emit(Events.BRIEF_SELECTED, el.dataset.briefId);
            });
        });
    }

    // U2: Create space via API (replaces promptNewSpace)
    async createSpace(name) {
        try {
            const space = await this.app.api.post('/api/spaces', { name });
            const spaces = this.app.state.get('spaces') || [];
            this.app.state.set('spaces', [...spaces, space]);
            this.app.state.set('currentSpace', space.id);
            this.app.events.emit(Events.SPACE_CREATED, space);
            this.app.events.emit(Events.SPACE_SELECTED, space.id);
            this.app.showToast(`Space "${name}" created`, 'success');
        } catch (error) {
            console.error('[Sidebar] Failed to create space:', error);
            this.app.showToast('Failed to create space', 'error');
        }
    }

    async loadAndRender() {
        try {
            const spaces = await this.app.api.get('/api/spaces');
            this.app.state.set('spaces', spaces);
        } catch (e) {
            console.error('[Sidebar] Failed to reload spaces:', e);
            this.app.showToast('Failed to reload spaces', 'error');
        }
        this.render();
    }

    spaceColor(name) {
        const colors = [
            'var(--accent-primary)',
            'var(--accent-search)',
            'var(--accent-success)',
            'var(--accent-warning)',
            'var(--accent-brief)',
            'var(--accent-source)'
        ];
        let hash = 0;
        for (let i = 0; i < name.length; i++) {
            hash = name.charCodeAt(i) + ((hash << 5) - hash);
        }
        return colors[Math.abs(hash) % colors.length];
    }

    sourceIcon(type) {
        const icons = {
            Rss: '&#9673;',
            YouTube: '&#9654;',
            Podcast: '&#9835;',
            GitHub: '&#10070;',
            HackerNews: '&#9650;',
            Reddit: '&#9673;',
            Bookmark: '&#9734;',
            Email: '&#9993;',
            FolderWatch: '&#128193;',
            Web: '&#127760;'
        };
        return icons[type] || '&#9679;';
    }
}
