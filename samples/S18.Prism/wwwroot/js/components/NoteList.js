/**
 * NoteList - Main content area showing note cards
 * Switches between Pulse view and Search results
 *
 * NOTE (U31): innerHTML re-creates entire DOM on each render.
 * A virtual DOM or targeted update approach would improve performance
 * for large lists but is acceptable overhead for this sample app.
 */

import { Events } from '../utils/EventBus.js';
import { escapeHtml, escapeAttr } from '../utils/html.js';

const ORIGIN_LABELS = {
    Upload: 'Upload',
    Capture: 'Capture',
    Source: 'Source',
    Brief: 'Brief',
    Digest: 'Digest',
    Generated: 'Generated'
};

const ORIGIN_CLASSES = {
    Upload: 'origin-upload',
    Capture: 'origin-capture',
    Source: 'origin-source',
    Brief: 'origin-brief',
    Digest: 'origin-digest',
    Generated: 'origin-generated'
};

export class NoteList {
    constructor(app) {
        this.app = app;
        this.container = document.getElementById('main-content');
        this.selectedNoteId = null;

        this.app.events.on(Events.SEARCH_RESULTS, (data) => this.renderSearchResults(data));
        this.app.events.on(Events.SEARCH_CLEAR, () => this.showPulse());
        this.app.events.on(Events.VIEW_CHANGED, (view) => {
            if (view === 'pulse') this.showPulse();
        });
        this.app.events.on(Events.NOTE_CREATED, () => this.refreshNotes());
    }

    /**
     * Render a grid of note cards
     * @param {Array} notes - Array of note objects
     * @param {string} headerHtml - Optional header to show above the grid
     */
    renderNoteGrid(notes, headerHtml = '') {
        if (notes.length === 0) {
            this.container.innerHTML = `
                ${headerHtml}
                <div class="empty-state">
                    <div class="empty-state-icon">&#128196;</div>
                    <p>No notes found</p>
                </div>
            `;
            return;
        }

        this.container.innerHTML = `
            ${headerHtml}
            <div class="note-grid">
                ${notes.map(note => this.renderCard(note)).join('')}
            </div>
        `;

        this.bindCardEvents();
    }

    renderCard(note) {
        const origin = note.origin || 'Upload';
        const originLabel = ORIGIN_LABELS[origin] || origin;
        const originClass = ORIGIN_CLASSES[origin] || 'origin-upload';
        const title = note.title || 'Untitled';
        const summary = note.summary || '';
        const concepts = note.keyConcepts || [];
        const category = note.category || '';
        const date = note.createdAt ? this.formatDate(note.createdAt) : '';
        const isSelected = note.id === this.selectedNoteId;

        // U27: tabindex and role for keyboard navigation
        return `
            <div class="note-card ${isSelected ? 'selected' : ''}"
                 data-note-id="${escapeAttr(note.id)}"
                 tabindex="0"
                 role="button">
                <div class="note-card-header">
                    <div class="note-card-title">${escapeHtml(title)}</div>
                    <span class="note-card-origin ${originClass}">${originLabel}</span>
                </div>
                ${summary ? `<div class="note-card-summary">${escapeHtml(summary)}</div>` : ''}
                ${concepts.length > 0 ? `
                    <div class="note-card-tags">
                        ${concepts.slice(0, 4).map(c => `<span class="tag">${escapeHtml(c)}</span>`).join('')}
                        ${concepts.length > 4 ? `<span class="tag">+${concepts.length - 4}</span>` : ''}
                    </div>
                ` : ''}
                <div class="note-card-footer">
                    ${category ? `<span class="note-card-category">${escapeHtml(category)}</span>` : '<span></span>'}
                    <span>${date}</span>
                </div>
            </div>
        `;
    }

    bindCardEvents() {
        this.container.querySelectorAll('.note-card').forEach(card => {
            const handler = () => {
                const noteId = card.dataset.noteId;
                this.selectNote(noteId);
            };
            card.addEventListener('click', handler);
            // U27: Keyboard support
            card.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handler();
                }
            });
        });
    }

    selectNote(noteId) {
        // Deselect previous
        if (this.selectedNoteId) {
            const prev = this.container.querySelector(`.note-card[data-note-id="${this.selectedNoteId}"]`);
            if (prev) prev.classList.remove('selected');
        }

        this.selectedNoteId = noteId;

        // Highlight current
        const current = this.container.querySelector(`.note-card[data-note-id="${noteId}"]`);
        if (current) current.classList.add('selected');

        this.app.events.emit(Events.NOTE_SELECTED, noteId);
    }

    renderSearchResults(data) {
        const headerHtml = `
            <div class="search-results-header">
                <span>
                    <span class="search-results-query">"${escapeHtml(data.query)}"</span>
                    &mdash; ${data.count} result${data.count !== 1 ? 's' : ''}
                </span>
            </div>
        `;

        if (data.error) {
            this.container.innerHTML = `
                ${headerHtml}
                <div class="empty-state">
                    <div class="empty-state-icon">&#9888;</div>
                    <p>Search failed</p>
                    <p class="text-sm text-tertiary">${escapeHtml(data.error)}</p>
                </div>
            `;
            return;
        }

        this.renderNoteGrid(data.results, headerHtml);
    }

    showPulse() {
        // Pulse component handles its own rendering
        this.app.components.pulse.render();
    }

    async refreshNotes() {
        const currentView = this.app.state.get('currentView');
        if (currentView === 'pulse') {
            this.app.components.pulse.render();
        }
    }

    formatDate(dateStr) {
        try {
            const date = new Date(dateStr);
            const now = new Date();
            const diffMs = now - date;
            const diffHrs = diffMs / (1000 * 60 * 60);

            if (diffHrs < 1) return `${Math.floor(diffMs / (1000 * 60))}m ago`;
            if (diffHrs < 24) return `${Math.floor(diffHrs)}h ago`;
            if (diffHrs < 48) return 'Yesterday';

            return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
        } catch {
            return '';
        }
    }
}
