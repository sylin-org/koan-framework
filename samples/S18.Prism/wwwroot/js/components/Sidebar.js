/**
 * Sidebar - Left panel with Spaces, Sources, Research Briefs
 */

import { Events } from '../utils/EventBus.js';

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
            <div class="sidebar-section">
                <div class="sidebar-heading">
                    <span>Spaces</span>
                    <button class="btn btn-ghost btn-sm" id="btn-add-space" title="New space">+</button>
                </div>
                ${spaces.map(space => `
                    <button class="sidebar-item ${space.id === currentSpaceId ? 'active' : ''}"
                            data-space-id="${this.escapeAttr(space.id)}">
                        <span class="space-dot" style="background: ${this.spaceColor(space.name)}"></span>
                        <span class="sidebar-item-label">${this.escapeHtml(space.name)}</span>
                    </button>
                `).join('')}
            </div>

            <div class="sidebar-section">
                <div class="sidebar-heading">
                    <span>Sources</span>
                    <span class="sidebar-item-count">${spaceSources.length}</span>
                </div>
                ${spaceSources.length === 0
                    ? '<div class="text-xs text-tertiary px-3 py-2">No sources configured</div>'
                    : spaceSources.map(source => `
                        <div class="sidebar-item" data-source-id="${this.escapeAttr(source.id)}">
                            <span class="sidebar-item-icon">${this.sourceIcon(source.type)}</span>
                            <span class="sidebar-item-label">${this.escapeHtml(source.name)}</span>
                            <span class="sidebar-item-count">${source.totalItemsPulled || 0}</span>
                        </div>
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
                        <div class="sidebar-item" data-brief-id="${this.escapeAttr(brief.id)}">
                            <span class="sidebar-item-icon" style="color: var(--accent-brief)">&#9671;</span>
                            <span class="sidebar-item-label">${this.escapeHtml(brief.name)}</span>
                            <span class="sidebar-item-count">${brief.totalItemsFound || 0}</span>
                        </div>
                    `).join('')}
            </div>
        `;

        this.bindEvents();
    }

    bindEvents() {
        // Space selection
        this.container.querySelectorAll('[data-space-id]').forEach(el => {
            el.addEventListener('click', () => {
                const spaceId = el.dataset.spaceId;
                this.app.state.set('currentSpace', spaceId);
                this.app.state.set('currentView', 'pulse');
                this.app.events.emit(Events.SPACE_SELECTED, spaceId);
            });
        });

        // Add space button
        const addBtn = this.container.querySelector('#btn-add-space');
        if (addBtn) {
            addBtn.addEventListener('click', () => this.promptNewSpace());
        }
    }

    async promptNewSpace() {
        const name = prompt('New space name:');
        if (!name || !name.trim()) return;

        try {
            const space = await this.app.api.post('/api/spaces', {
                name: name.trim()
            });
            const spaces = this.app.state.get('spaces') || [];
            this.app.state.set('spaces', [...spaces, space]);
            this.app.state.set('currentSpace', space.id);
            this.app.events.emit(Events.SPACE_CREATED, space);
            this.app.events.emit(Events.SPACE_SELECTED, space.id);
        } catch (error) {
            console.error('[Sidebar] Failed to create space:', error);
        }
    }

    async loadAndRender() {
        try {
            const spaces = await this.app.api.get('/api/spaces');
            this.app.state.set('spaces', spaces);
        } catch (e) {
            console.error('[Sidebar] Failed to reload spaces:', e);
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

    escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    escapeAttr(str) {
        return String(str).replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
}
