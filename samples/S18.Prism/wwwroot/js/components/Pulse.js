/**
 * Pulse - "What's new" briefing view
 * Shows recent notes grouped by origin and pending research findings
 */

import { Events } from '../utils/EventBus.js';
import { escapeHtml, escapeAttr } from '../utils/html.js';

export class Pulse {
    constructor(app) {
        this.app = app;
        this.container = document.getElementById('main-content');
        this.pulseData = null;
        this.recentNotes = [];
        this.pageSize = 20;
        this.currentOffset = 0;
        this.hasMore = false;

        this.app.events.on(Events.SPACE_SELECTED, () => {
            this.currentOffset = 0;
            this.recentNotes = [];
            this.loadAndRender();
        });
        this.app.events.on(Events.NOTE_CREATED, () => {
            this.currentOffset = 0;
            this.recentNotes = [];
            this.loadAndRender();
        });
        this.app.events.on(Events.VIEW_CHANGED, (view) => {
            if (view === 'pulse') {
                this.currentOffset = 0;
                this.recentNotes = [];
                this.loadAndRender();
            }
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
                this.loadRecentNotes(spaceId, 0)
            ]);

            this.pulseData = pulseData;
            this.recentNotes = notesResponse || [];
            this.currentOffset = this.recentNotes.length;
            this.hasMore = this.recentNotes.length >= this.pageSize;
            this.render();
        } catch (error) {
            console.error('[Pulse] Failed to load pulse:', error);
            this.app.showToast('Failed to load pulse', 'error');
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

    async loadRecentNotes(spaceId, offset) {
        try {
            const notes = await this.app.api.get('/api/notes', {
                'filter[spaceId]': spaceId,
                sort: '-id',
                pageSize: this.pageSize,
                page: Math.floor(offset / this.pageSize) + 1
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
                        ${escapeHtml(pulse.summary)}
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

                ${this.hasMore ? `
                    <div class="load-more-container">
                        <button class="btn-load-more" id="pulse-load-more">Load more</button>
                    </div>
                ` : ''}
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
            <div class="pulse-item" data-note-id="${escapeAttr(note.id)}" tabindex="0" role="button">
                <div class="pulse-item-title">${escapeHtml(title)}</div>
                ${summary ? `<div class="pulse-item-summary">${escapeHtml(summary)}</div>` : ''}
                <div class="pulse-item-meta">
                    <span>${date}</span>
                    ${concepts.length > 0
                        ? `<span>${concepts.slice(0, 3).map(c => escapeHtml(c)).join(' / ')}</span>`
                        : ''}
                </div>
            </div>
        `;
    }

    renderFinding(finding) {
        const relevancePct = Math.round((finding.relevanceScore || 0) * 100);

        return `
            <div class="finding-card" data-finding-id="${escapeAttr(finding.id)}">
                <div class="flex items-center justify-between">
                    <div class="pulse-item-title">${escapeHtml(finding.title || '')}</div>
                    <span class="finding-relevance">${relevancePct}% relevant</span>
                </div>
                ${finding.summary ? `<div class="pulse-item-summary">${escapeHtml(finding.summary)}</div>` : ''}
                ${finding.whyRelevant ? `<div class="finding-why">${escapeHtml(finding.whyRelevant)}</div>` : ''}
                <div class="pulse-item-meta">
                    ${finding.sourceName ? `<span>${escapeHtml(finding.sourceName)}</span>` : ''}
                    ${finding.publishedAt ? `<span>${this.formatDate(finding.publishedAt)}</span>` : ''}
                </div>
                <div class="pulse-item-actions" data-finding-actions="${escapeAttr(finding.id)}">
                    <button class="btn btn-sm btn-success" data-action="approve" data-finding-id="${escapeAttr(finding.id)}">
                        Approve
                    </button>
                    <button class="btn btn-sm btn-ghost" data-action="dismiss" data-finding-id="${escapeAttr(finding.id)}">
                        Dismiss
                    </button>
                    ${finding.url ? `
                        <a class="btn btn-sm btn-ghost" href="${escapeAttr(finding.url)}"
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
                    <p class="text-sm">Upload a file, paste text, or add a URL using the Add Note button above.</p>
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
            const handler = () => {
                this.app.events.emit(Events.NOTE_SELECTED, el.dataset.noteId);
            };
            el.addEventListener('click', handler);
            el.style.cursor = 'pointer';
            // U27: keyboard navigation for pulse items
            el.addEventListener('keydown', (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handler();
                }
            });
        });

        // Approve findings
        this.container.querySelectorAll('[data-action="approve"]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.approveFinding(btn.dataset.findingId);
            });
        });

        // U8: Dismiss with undo pattern
        this.container.querySelectorAll('[data-action="dismiss"]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const card = this.container.querySelector(
                    `.finding-card[data-finding-id="${btn.dataset.findingId}"]`);
                if (card) {
                    this.dismissFinding(btn.dataset.findingId, card);
                }
            });
        });

        // U17: Load more button
        const loadMoreBtn = this.container.querySelector('#pulse-load-more');
        if (loadMoreBtn) {
            loadMoreBtn.addEventListener('click', () => this.loadMore());
        }
    }

    // U17: Load next page of notes
    async loadMore() {
        const spaceId = this.app.state.get('currentSpace');
        if (!spaceId) return;

        const moreNotes = await this.loadRecentNotes(spaceId, this.currentOffset);
        if (moreNotes.length > 0) {
            this.recentNotes = [...this.recentNotes, ...moreNotes];
            this.currentOffset += moreNotes.length;
            this.hasMore = moreNotes.length >= this.pageSize;
            this.render();
        } else {
            this.hasMore = false;
            this.render();
        }
    }

    async approveFinding(findingId) {
        try {
            await this.app.api.post(`/api/findings/${findingId}/approve`);

            // Update UI optimistically
            const card = this.container.querySelector(`.finding-card[data-finding-id="${findingId}"]`);
            if (card) {
                card.classList.add('approved');
                const actions = card.querySelector(`[data-finding-actions="${findingId}"]`);
                if (actions) {
                    actions.innerHTML =
                        '<span class="text-xs" style="color: var(--accent-success)">Approved</span>';
                }
            }

            this.app.events.emit(Events.FINDING_APPROVED, findingId);
            this.app.showToast('Finding approved', 'success');
        } catch (error) {
            console.error('[Pulse] Failed to approve finding:', error);
            this.app.showToast('Failed to approve finding', 'error');
        }
    }

    // U8: Dismiss with undo pattern
    async dismissFinding(findingId, card) {
        card.classList.add('dismissed');
        const actions = card.querySelector(`[data-finding-actions="${findingId}"]`);
        if (!actions) return;

        const originalHtml = actions.innerHTML;
        actions.innerHTML = '<span class="text-secondary text-xs">Dismissed</span> <button class="btn-undo">Undo</button>';

        const timeout = setTimeout(async () => {
            try {
                await this.app.api.post(`/api/findings/${findingId}/dismiss`);
                this.app.events.emit(Events.FINDING_DISMISSED, findingId);
            } catch (error) {
                console.error('[Pulse] Failed to dismiss finding:', error);
                this.app.showToast('Failed to dismiss finding', 'error');
                // Restore on failure
                card.classList.remove('dismissed');
                actions.innerHTML = originalHtml;
                this.rebindFindingActions(card);
            }
        }, 5000);

        const undoBtn = actions.querySelector('.btn-undo');
        if (undoBtn) {
            undoBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                clearTimeout(timeout);
                card.classList.remove('dismissed');
                actions.innerHTML = originalHtml;
                this.rebindFindingActions(card);
            });
        }
    }

    rebindFindingActions(card) {
        const approveBtn = card.querySelector('[data-action="approve"]');
        if (approveBtn) {
            approveBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.approveFinding(approveBtn.dataset.findingId);
            });
        }
        const dismissBtn = card.querySelector('[data-action="dismiss"]');
        if (dismissBtn) {
            dismissBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.dismissFinding(dismissBtn.dataset.findingId, card);
            });
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
}
