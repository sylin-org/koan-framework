/**
 * Prism — Personal Knowledge Intelligence
 * Main Application Entry Point
 */

import { API } from './api.js';
import { EventBus, Events } from './utils/EventBus.js';
import { StateManager } from './utils/StateManager.js';
import { Sidebar } from './components/Sidebar.js';
import { NoteList } from './components/NoteList.js';
import { NoteDetail } from './components/NoteDetail.js';
import { SearchBar } from './components/SearchBar.js';
import { Upload } from './components/Upload.js';
import { Pulse } from './components/Pulse.js';

class PrismApp {
    constructor() {
        this.api = new API();
        this.events = new EventBus();
        this.state = new StateManager();
        this.components = {};
    }

    async init() {
        console.log('[Prism] Initializing...');

        // Load spaces
        try {
            const spaces = await this.api.get('/api/spaces');
            this.state.set('spaces', Array.isArray(spaces) ? spaces : []);
        } catch (error) {
            console.error('[Prism] Failed to load spaces:', error);
            this.state.set('spaces', []);
        }

        const spaces = this.state.get('spaces');
        this.state.set('currentSpace', spaces[0]?.id || null);
        this.state.set('currentView', 'pulse');

        // Load sources and briefs for sidebar
        await this.loadSidebarData();

        // Initialize components
        this.components.sidebar = new Sidebar(this);
        this.components.noteList = new NoteList(this);
        this.components.noteDetail = new NoteDetail(this);
        this.components.searchBar = new SearchBar(this);
        this.components.upload = new Upload(this);
        this.components.pulse = new Pulse(this);

        // Wire up status bar updates
        this.events.on(Events.STATUS_UPDATE, (msg) => {
            const statusText = document.getElementById('status-text');
            if (statusText) statusText.textContent = msg;
        });

        // Reload sidebar data on space change
        this.events.on(Events.SPACE_SELECTED, () => this.loadSidebarData());

        // Update counts in status bar
        await this.updateCounts();

        // Load initial pulse view
        this.events.emit(Events.SPACE_SELECTED, this.state.get('currentSpace'));

        console.log('[Prism] Ready');
    }

    async loadSidebarData() {
        try {
            const [sources, briefs] = await Promise.all([
                this.api.get('/api/sources').catch(() => []),
                this.api.get('/api/briefs').catch(() => [])
            ]);
            this.state.set('sources', Array.isArray(sources) ? sources : []);
            this.state.set('briefs', Array.isArray(briefs) ? briefs : []);
        } catch (error) {
            console.error('[Prism] Failed to load sidebar data:', error);
            this.state.set('sources', []);
            this.state.set('briefs', []);
        }
    }

    async updateCounts() {
        try {
            const spaces = this.state.get('spaces') || [];
            const countsEl = document.getElementById('status-counts');
            if (countsEl) {
                countsEl.textContent = `${spaces.length} space${spaces.length !== 1 ? 's' : ''}`;
            }
        } catch {
            // ignore
        }
    }
}

// Initialize app when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.app = new PrismApp();
        window.app.init();
    });
} else {
    window.app = new PrismApp();
    window.app.init();
}
