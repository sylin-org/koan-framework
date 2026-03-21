/**
 * NoteDetail - Right panel showing selected note details
 */

import { Events } from '../utils/EventBus.js';

export class NoteDetail {
    constructor(app) {
        this.app = app;
        this.panel = document.getElementById('detail-panel');
        this.currentNote = null;

        this.app.events.on(Events.NOTE_SELECTED, (noteId) => this.loadNote(noteId));
        this.app.events.on(Events.NOTE_DESELECTED, () => this.close());
    }

    async loadNote(noteId) {
        try {
            const note = await this.app.api.get(`/api/notes/${noteId}`);
            this.currentNote = note;
            this.render();
            this.open();
        } catch (error) {
            console.error('[NoteDetail] Failed to load note:', error);
        }
    }

    open() {
        this.panel.classList.add('open');
    }

    close() {
        this.panel.classList.remove('open');
        this.currentNote = null;
    }

    render() {
        const note = this.currentNote;
        if (!note) {
            this.panel.innerHTML = '';
            return;
        }

        const title = note.title || 'Untitled';
        const summary = note.summary || '';
        const blocks = note.blocks || [];
        const concepts = note.keyConcepts || [];
        const analysis = note.analysis;
        const sourceUrl = note.sourceUrl;
        const rating = note.userRating || 0;
        const category = note.category || '';
        const origin = note.origin || 'Upload';
        const createdAt = note.createdAt ? new Date(note.createdAt).toLocaleString() : '';
        const publishedAt = note.sourcePublishedAt
            ? new Date(note.sourcePublishedAt).toLocaleString() : '';

        this.panel.innerHTML = `
            <div class="fade-in">
                <div class="detail-header">
                    <div class="detail-title">${this.escapeHtml(title)}</div>
                    <button class="btn-close" id="btn-close-detail">&times;</button>
                </div>

                <div class="detail-body">
                    <!-- Summary -->
                    ${summary ? `
                        <div class="detail-section">
                            <div class="detail-section-title">Summary</div>
                            <div class="detail-summary">${this.escapeHtml(summary)}</div>
                        </div>
                    ` : ''}

                    <!-- Content blocks -->
                    ${blocks.length > 0 ? `
                        <div class="detail-section">
                            <div class="detail-section-title">Content</div>
                            ${blocks.map(block => this.renderBlock(block)).join('')}
                        </div>
                    ` : ''}

                    <!-- Key concepts -->
                    ${concepts.length > 0 ? `
                        <div class="detail-section">
                            <div class="detail-section-title">Key Concepts</div>
                            <div class="detail-tags">
                                ${concepts.map(c => `<span class="tag">${this.escapeHtml(c)}</span>`).join('')}
                            </div>
                        </div>
                    ` : ''}

                    <!-- Analysis -->
                    ${analysis ? this.renderAnalysis(analysis) : ''}

                    <!-- Source info -->
                    <div class="detail-section">
                        <div class="detail-section-title">Info</div>
                        <div class="detail-meta">
                            <div class="detail-meta-row">
                                <span>Origin</span><span>${this.escapeHtml(origin)}</span>
                            </div>
                            ${category ? `
                                <div class="detail-meta-row">
                                    <span>Category</span><span>${this.escapeHtml(category)}</span>
                                </div>
                            ` : ''}
                            ${createdAt ? `
                                <div class="detail-meta-row">
                                    <span>Added</span><span>${createdAt}</span>
                                </div>
                            ` : ''}
                            ${publishedAt ? `
                                <div class="detail-meta-row">
                                    <span>Published</span><span>${publishedAt}</span>
                                </div>
                            ` : ''}
                        </div>
                    </div>

                    <!-- Source URL -->
                    ${sourceUrl ? `
                        <div class="detail-section">
                            <a class="detail-link" href="${this.escapeAttr(sourceUrl)}"
                               target="_blank" rel="noopener noreferrer">
                                Open original &#8599;
                            </a>
                        </div>
                    ` : ''}

                    <!-- Rating -->
                    <div class="detail-section">
                        <div class="detail-section-title">Rating</div>
                        <div class="rating" id="detail-rating">
                            ${[1, 2, 3, 4, 5].map(star => `
                                <button class="rating-star ${star <= rating ? 'active' : ''}"
                                        data-rating="${star}"
                                        title="${star} star${star !== 1 ? 's' : ''}">
                                    ${star <= rating ? '&#9733;' : '&#9734;'}
                                </button>
                            `).join('')}
                        </div>
                    </div>
                </div>
            </div>
        `;

        this.bindEvents();
    }

    renderBlock(block) {
        const kind = block.kind || 'Text';
        const content = block.content || '';

        switch (kind) {
            case 'Text':
                return `<div class="detail-block">${this.escapeHtml(content)}</div>`;

            case 'Table':
                return `<div class="detail-block detail-block-table">${this.renderTableContent(block)}</div>`;

            case 'Image':
                return `<div class="detail-block">
                    <div class="text-xs text-tertiary mb-2">Image description</div>
                    ${this.escapeHtml(content)}
                </div>`;

            case 'Audio':
                return `<div class="detail-block">
                    <div class="text-xs text-tertiary mb-2">Audio transcript</div>
                    ${this.escapeHtml(content)}
                </div>`;

            case 'Data':
                return `<div class="detail-block detail-block-code"><pre>${this.escapeHtml(content)}</pre></div>`;

            default:
                return `<div class="detail-block">${this.escapeHtml(content)}</div>`;
        }
    }

    renderTableContent(block) {
        // Try to render structured content as HTML table, fall back to text
        if (block.structuredContent) {
            try {
                const data = JSON.parse(block.structuredContent);
                if (Array.isArray(data) && data.length > 0) {
                    const headers = Object.keys(data[0]);
                    return `
                        <table>
                            <thead>
                                <tr>${headers.map(h => `<th>${this.escapeHtml(h)}</th>`).join('')}</tr>
                            </thead>
                            <tbody>
                                ${data.map(row => `
                                    <tr>${headers.map(h => `<td>${this.escapeHtml(String(row[h] ?? ''))}</td>`).join('')}</tr>
                                `).join('')}
                            </tbody>
                        </table>
                    `;
                }
            } catch {
                // Fall through to text rendering
            }
        }
        return `<pre>${this.escapeHtml(block.content)}</pre>`;
    }

    renderAnalysis(analysis) {
        const sections = [];

        if (analysis.actionItems?.length > 0) {
            sections.push(this.renderAnalysisList('Action Items', analysis.actionItems));
        }
        if (analysis.people?.length > 0) {
            sections.push(this.renderAnalysisList('People', analysis.people));
        }
        if (analysis.organizations?.length > 0) {
            sections.push(this.renderAnalysisList('Organizations', analysis.organizations));
        }
        if (analysis.questions?.length > 0) {
            sections.push(this.renderAnalysisList('Questions', analysis.questions));
        }
        if (analysis.references?.length > 0) {
            sections.push(this.renderAnalysisList('References', analysis.references));
        }

        if (sections.length === 0) return '';

        return `
            <div class="detail-section">
                <div class="detail-section-title">Analysis</div>
                ${sections.join('')}
            </div>
        `;
    }

    renderAnalysisList(title, items) {
        return `
            <div class="mb-2">
                <div class="text-xs text-tertiary mb-2">${this.escapeHtml(title)}</div>
                <ul class="detail-analysis-list">
                    ${items.map(item => `<li>${this.escapeHtml(item)}</li>`).join('')}
                </ul>
            </div>
        `;
    }

    bindEvents() {
        // Close button
        const closeBtn = this.panel.querySelector('#btn-close-detail');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => {
                this.close();
                this.app.events.emit(Events.NOTE_DESELECTED);
            });
        }

        // Rating stars
        const ratingEl = this.panel.querySelector('#detail-rating');
        if (ratingEl) {
            ratingEl.querySelectorAll('.rating-star').forEach(star => {
                star.addEventListener('click', () => {
                    const rating = parseInt(star.dataset.rating, 10);
                    this.rateNote(rating);
                });
            });
        }
    }

    async rateNote(rating) {
        if (!this.currentNote) return;

        try {
            await this.app.api.put(`/api/notes/${this.currentNote.id}`, {
                ...this.currentNote,
                userRating: rating
            });
            this.currentNote = { ...this.currentNote, userRating: rating };
            this.render();
        } catch (error) {
            console.error('[NoteDetail] Failed to rate note:', error);
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
