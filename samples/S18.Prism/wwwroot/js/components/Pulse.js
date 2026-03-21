/**
 * Pulse - "What's new" briefing view
 * Shows recent notes grouped by origin and pending research findings
 */

import { Events } from '../utils/EventBus.js';

export class Pulse {
    constructor(app) {
        this.app = app;
        this.container = document.getElementById('main-content');
        this.pulseData = null;

        this.app.events.on(Events.SPACE_SELECTED, () => this.loadAndRender());
        this.app.events.on(Events.NOTE_CREATED, () => this.loadAndRender());
        this.app.events.on(Events.VIEW_CHANGED, (view) => {
            if (view === 'pulse') this.loadAndRender();
        });
    }

    async loadAndRender() {
        const spaceId = this.app.state.get('currentSpace');
        if (!spaceId) {
            this.renderEmpty();
            return;
        }

        this.renderLoading();

        try {
            const [pulseData, notesResponse] = await Promise.all([
                this.loadPulse(spaceId),
                this.loadRecentNotes(spaceId)
            ]);

            this.pulseData = pulseData;
            this.recentNotes = notesResponse || [];
            this.render();
        } catch (error) {
            console.error('[Pulse] Failed to load pulse:', error);
            // Still try to render with whatever we have
            this.render();
        }
    }

    async loadPulse(spaceId) {
        try {
            return await this.app.api.get(`/api/pulse/${spaceId}`);
        } catch (error) {
            console.error('[Pulse] Pulse API failed:', error);
            return null;
        }
    }

    async loadRecentNotes(spaceId) {
        try {
            const notes = await this.app.api.get('/api/notes', {
                'filter[spaceId]': spaceId,
                sort: '-id',
                pageSize: 20
            });
            return Array.isArray(notes) ? notes : [];
        } catch (error) {
            console.error('[Pulse] Notes API failed:', error);
            return [];
        }
    }

    render() {
        const notes = this.recentNotes || [];
        const pulse = this.pulseData;

        if (notes.length === 0 && !pulse) {
            this.renderEmpty();
            return;
        }

        // Group notes by origin
        const groups = this.groupByOrigin(notes);

        // Get pending findings from pulse data
        const findings = pulse?.findings || [];
        const pendingFindings = findings.filter(f =>
            f.status === 'PendingReview' || f.reviewStatus === 'Pending'
        );

        this.container.innerHTML = `
            <div class="pulse-container">
                <div class="pulse-header">
                    <span class="pulse-header-icon">&#9672;</span>
                    <h2>Pulse</h2>
                </div>

                ${pulse?.summary ? `
                    <div class="detail-block">
                        ${this.escapeHtml(pulse.summary)}
                    </div>
                ` : ''}

                ${pendingFindings.length > 0 ? `
                    <div class="pulse-group">
                        <div class="pulse-group-title">
                            <span class="group-icon" style="color: var(--accent-warning)">&#9671;</span>
                            Pending Review
                            <span class="badge badge-warning">${pendingFindings.length}</span>
                        </div>
                        ${pendingFindings.map(f => this.renderFinding(f)).join('')}
                    </div>
                ` : ''}

                ${Object.entries(groups).map(([origin, groupNotes]) => `
                    <div class="pulse-group">
                        <div class="pulse-group-title">
                            <span class="group-icon">${this.originIcon(origin)}</span>
                            ${origin}
                            <span class="sidebar-item-count">${groupNotes.length}</span>
                        </div>
                        ${groupNotes.map(note => this.renderPulseItem(note)).join('')}
                    </div>
                `).join('')}
            </div>
        `;

        this.bindEvents();

        // Update status counts
        this.app.events.emit(Events.STATUS_UPDATE,
            `${notes.length} note${notes.length !== 1 ? 's' : ''} in this space`);
    }

    renderPulseItem(note) {
        const title = note.title || 'Untitled';
        const summary = note.summary || '';
        const date = note.createdAt ? this.formatDate(note.createdAt) : '';
        const concepts = note.keyConcepts || [];

        return `
            <div class="pulse-item" data-note-id="${this.escapeAttr(note.id)}">
                <div class="pulse-item-title">${this.escapeHtml(title)}</div>
                ${summary ? `<div class="pulse-item-summary">${this.escapeHtml(summary)}</div>` : ''}
                <div class="pulse-item-meta">
                    <span>${date}</span>
                    ${concepts.length > 0
                        ? `<span>${concepts.slice(0, 3).map(c => this.escapeHtml(c)).join(' / ')}</span>`
                        : ''}
                </div>
            </div>
        `;
    }

    renderFinding(finding) {
        const relevancePct = Math.round((finding.relevanceScore || 0) * 100);

        return `
            <div class="finding-card" data-finding-id="${this.escapeAttr(finding.id)}">
                <div class="flex items-center justify-between">
                    <div class="pulse-item-title">${this.escapeHtml(finding.title || '')}</div>
                    <span class="finding-relevance">${relevancePct}% relevant</span>
                </div>
                ${finding.summary ? `<div class="pulse-item-summary">${this.escapeHtml(finding.summary)}</div>` : ''}
                ${finding.whyRelevant ? `<div class="finding-why">${this.escapeHtml(finding.whyRelevant)}</div>` : ''}
                <div class="pulse-item-meta">
                    ${finding.sourceName ? `<span>${this.escapeHtml(finding.sourceName)}</span>` : ''}
                    ${finding.publishedAt ? `<span>${this.formatDate(finding.publishedAt)}</span>` : ''}
                </div>
                <div class="pulse-item-actions">
                    <button class="btn btn-sm btn-success" data-action="approve" data-finding-id="${this.escapeAttr(finding.id)}">
                        Approve
                    </button>
                    <button class="btn btn-sm btn-ghost" data-action="dismiss" data-finding-id="${this.escapeAttr(finding.id)}">
                        Dismiss
                    </button>
                    ${finding.url ? `
                        <a class="btn btn-sm btn-ghost" href="${this.escapeAttr(finding.url)}"
                           target="_blank" rel="noopener noreferrer">
                            Open &#8599;
                        </a>
                    ` : ''}
                </div>
            </div>
        `;
    }

    renderEmpty() {
        this.container.innerHTML = `
            <div class="pulse-container">
                <div class="pulse-header">
                    <span class="pulse-header-icon">&#9672;</span>
                    <h2>Pulse</h2>
                </div>
                <div class="pulse-empty">
                    <div class="pulse-empty-icon">&#128218;</div>
                    <p>No notes yet. Add something to get started.</p>
                    <p class="text-sm">Upload a file, paste text, or add a URL using the Add button above.</p>
                </div>
            </div>
        `;
    }

    renderLoading() {
        this.container.innerHTML = `
            <div class="pulse-container">
                <div class="pulse-header">
                    <span class="pulse-header-icon">&#9672;</span>
                    <h2>Pulse</h2>
                </div>
                <div class="loading-spinner">
                    <div class="spinner"></div>
                    <span>Loading...</span>
                </div>
            </div>
        `;
    }

    bindEvents() {
        // Click on pulse items to select note
        this.container.querySelectorAll('.pulse-item[data-note-id]').forEach(el => {
            el.addEventListener('click', () => {
                this.app.events.emit(Events.NOTE_SELECTED, el.dataset.noteId);
            });
            el.style.cursor = 'pointer';
        });

        // Approve/dismiss findings
        this.container.querySelectorAll('[data-action="approve"]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.approveFinding(btn.dataset.findingId);
            });
        });

        this.container.querySelectorAll('[data-action="dismiss"]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.dismissFinding(btn.dataset.findingId);
            });
        });
    }

    async approveFinding(findingId) {
        try {
            const finding = await this.app.api.get(`/api/briefs/${findingId}`);
            await this.app.api.put(`/api/briefs/${findingId}`, {
                ...finding,
                status: 'Approved',
                reviewStatus: 'Approved'
            });

            // Update UI optimistically
            const card = this.container.querySelector(`.finding-card[data-finding-id="${findingId}"]`);
            if (card) {
                card.classList.add('approved');
                card.querySelector('.pulse-item-actions').innerHTML =
                    '<span class="text-xs" style="color: var(--accent-success)">Approved</span>';
            }

            this.app.events.emit(Events.FINDING_APPROVED, findingId);
        } catch (error) {
            console.error('[Pulse] Failed to approve finding:', error);
        }
    }

    async dismissFinding(findingId) {
        try {
            const finding = await this.app.api.get(`/api/briefs/${findingId}`);
            await this.app.api.put(`/api/briefs/${findingId}`, {
                ...finding,
                status: 'Dismissed',
                reviewStatus: 'Rejected'
            });

            const card = this.container.querySelector(`.finding-card[data-finding-id="${findingId}"]`);
            if (card) {
                card.classList.add('dismissed');
                card.querySelector('.pulse-item-actions').innerHTML =
                    '<span class="text-xs text-tertiary">Dismissed</span>';
            }

            this.app.events.emit(Events.FINDING_DISMISSED, findingId);
        } catch (error) {
            console.error('[Pulse] Failed to dismiss finding:', error);
        }
    }

    groupByOrigin(notes) {
        const groups = {};
        for (const note of notes) {
            const origin = note.origin || 'Upload';
            if (!groups[origin]) groups[origin] = [];
            groups[origin].push(note);
        }
        return groups;
    }

    originIcon(origin) {
        const icons = {
            Upload: '&#128196;',
            Capture: '&#128247;',
            Source: '&#128279;',
            Brief: '&#9671;',
            Digest: '&#128220;',
            Generated: '&#9881;'
        };
        return icons[origin] || '&#9679;';
    }

    formatDate(dateStr) {
        try {
            const date = new Date(dateStr);
            const now = new Date();
            const diffMs = now - date;
            const diffHrs = diffMs / (1000 * 60 * 60);

            if (diffHrs < 1) return `${Math.max(1, Math.floor(diffMs / (1000 * 60)))}m ago`;
            if (diffHrs < 24) return `${Math.floor(diffHrs)}h ago`;
            if (diffHrs < 48) return 'Yesterday';

            return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
        } catch {
            return '';
        }
    }

    escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str || '';
        return div.innerHTML;
    }

    escapeAttr(str) {
        return String(str || '').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
}
