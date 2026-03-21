/**
 * Upload - Modal with File, Text, URL tabs for adding content to Prism
 */

import { Events } from '../utils/EventBus.js';

export class Upload {
    constructor(app) {
        this.app = app;
        this.modal = document.getElementById('upload-modal');
        this.activeTab = 'file';

        this.setupAddButton();
        this.setupModal();
    }

    setupAddButton() {
        const addBtn = document.getElementById('btn-add');
        if (addBtn) {
            addBtn.addEventListener('click', () => this.open());
        }
    }

    setupModal() {
        // Close button
        const closeBtn = document.getElementById('btn-close-upload');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => this.close());
        }

        // Close on overlay click
        this.modal.addEventListener('click', (e) => {
            if (e.target === this.modal) this.close();
        });

        // Tab switching
        this.modal.querySelectorAll('.tab').forEach(tab => {
            tab.addEventListener('click', () => {
                this.switchTab(tab.dataset.tab);
            });
        });

        // Drop zone
        const dropZone = document.getElementById('drop-zone');
        const fileInput = document.getElementById('file-input');

        if (dropZone && fileInput) {
            dropZone.addEventListener('click', () => fileInput.click());

            dropZone.addEventListener('dragover', (e) => {
                e.preventDefault();
                dropZone.classList.add('drag-over');
            });

            dropZone.addEventListener('dragleave', () => {
                dropZone.classList.remove('drag-over');
            });

            dropZone.addEventListener('drop', (e) => {
                e.preventDefault();
                dropZone.classList.remove('drag-over');
                if (e.dataTransfer.files.length > 0) {
                    this.handleFiles(e.dataTransfer.files);
                }
            });

            fileInput.addEventListener('change', () => {
                if (fileInput.files.length > 0) {
                    this.handleFiles(fileInput.files);
                }
            });
        }

        // Submit button
        const submitBtn = document.getElementById('btn-submit-upload');
        if (submitBtn) {
            submitBtn.addEventListener('click', () => this.submit());
        }

        // Close on Escape
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !this.modal.hidden) {
                this.close();
            }
        });
    }

    open() {
        this.updateSpaceOptions();
        this.modal.hidden = false;
        this.resetState();
    }

    close() {
        this.modal.hidden = true;
        this.resetState();
    }

    resetState() {
        this.pendingFiles = null;
        this.setProgress(false);
        this.setStatus('');

        const fileInput = document.getElementById('file-input');
        if (fileInput) fileInput.value = '';

        const textInput = document.getElementById('text-input');
        if (textInput) textInput.value = '';

        const titleInput = document.getElementById('text-title');
        if (titleInput) titleInput.value = '';

        const urlInput = document.getElementById('url-input');
        if (urlInput) urlInput.value = '';

        const dropZone = document.getElementById('drop-zone');
        if (dropZone) dropZone.querySelector('p').textContent = 'Drop files here or click to browse';
    }

    switchTab(tabName) {
        this.activeTab = tabName;

        // Update tab buttons
        this.modal.querySelectorAll('.tab').forEach(tab => {
            tab.classList.toggle('active', tab.dataset.tab === tabName);
        });

        // Update panels
        this.modal.querySelectorAll('.tab-panel').forEach(panel => {
            panel.classList.toggle('active', panel.dataset.panel === tabName);
        });
    }

    updateSpaceOptions() {
        const select = document.getElementById('upload-space-select');
        if (!select) return;

        const spaces = this.app.state.get('spaces') || [];
        const currentSpace = this.app.state.get('currentSpace');

        select.innerHTML = spaces.map(s => `
            <option value="${this.escapeAttr(s.id)}" ${s.id === currentSpace ? 'selected' : ''}>
                ${this.escapeHtml(s.name)}
            </option>
        `).join('');
    }

    handleFiles(fileList) {
        this.pendingFiles = fileList;
        const dropZone = document.getElementById('drop-zone');
        if (dropZone) {
            const names = Array.from(fileList).map(f => f.name).join(', ');
            dropZone.querySelector('p').textContent = `${fileList.length} file${fileList.length > 1 ? 's' : ''}: ${names}`;
        }
    }

    async submit() {
        const spaceSelect = document.getElementById('upload-space-select');
        const spaceId = spaceSelect?.value;

        if (!spaceId) {
            this.setStatus('Please select a space');
            return;
        }

        try {
            switch (this.activeTab) {
                case 'file':
                    await this.submitFiles(spaceId);
                    break;
                case 'text':
                    await this.submitText(spaceId);
                    break;
                case 'url':
                    await this.submitUrl(spaceId);
                    break;
            }
        } catch (error) {
            console.error('[Upload] Submit failed:', error);
            this.setStatus(`Error: ${error.message}`);
            this.app.events.emit(Events.UPLOAD_ERROR, error.message);
        }
    }

    async submitFiles(spaceId) {
        if (!this.pendingFiles || this.pendingFiles.length === 0) {
            this.setStatus('No files selected');
            return;
        }

        this.setProgress(true);
        this.app.events.emit(Events.UPLOAD_STARTED);

        for (let i = 0; i < this.pendingFiles.length; i++) {
            const file = this.pendingFiles[i];
            this.setStatus(`Uploading ${file.name} (${i + 1}/${this.pendingFiles.length})...`);

            const formData = new FormData();
            formData.append('file', file);

            await this.app.api.upload(
                `/api/notes/upload?spaceId=${encodeURIComponent(spaceId)}`,
                formData,
                (pct) => this.setProgressValue(pct)
            );
        }

        this.setStatus('Upload complete');
        this.app.events.emit(Events.UPLOAD_COMPLETE);
        this.app.events.emit(Events.NOTE_CREATED);

        setTimeout(() => this.close(), 1000);
    }

    async submitText(spaceId) {
        const textInput = document.getElementById('text-input');
        const titleInput = document.getElementById('text-title');
        const text = textInput?.value?.trim();
        const title = titleInput?.value?.trim() || null;

        if (!text) {
            this.setStatus('Please enter some text');
            return;
        }

        this.setProgress(true);
        this.setStatus('Ingesting text...');
        this.app.events.emit(Events.UPLOAD_STARTED);

        await this.app.api.post('/api/notes/text', { spaceId, text, title });

        this.setStatus('Text added');
        this.app.events.emit(Events.UPLOAD_COMPLETE);
        this.app.events.emit(Events.NOTE_CREATED);

        setTimeout(() => this.close(), 1000);
    }

    async submitUrl(spaceId) {
        const urlInput = document.getElementById('url-input');
        const url = urlInput?.value?.trim();

        if (!url) {
            this.setStatus('Please enter a URL');
            return;
        }

        this.setProgress(true);
        this.setStatus('Fetching URL...');
        this.app.events.emit(Events.UPLOAD_STARTED);

        await this.app.api.post('/api/notes/url', { spaceId, url });

        this.setStatus('URL ingested');
        this.app.events.emit(Events.UPLOAD_COMPLETE);
        this.app.events.emit(Events.NOTE_CREATED);

        setTimeout(() => this.close(), 1000);
    }

    setProgress(visible) {
        const el = document.getElementById('upload-progress');
        if (el) el.classList.toggle('hidden', !visible);
        if (!visible) this.setProgressValue(0);
    }

    setProgressValue(pct) {
        const bar = document.getElementById('upload-progress-bar');
        if (bar) bar.style.width = `${Math.round(pct)}%`;
    }

    setStatus(msg) {
        const el = document.getElementById('upload-status');
        if (el) {
            el.textContent = msg;
            el.classList.toggle('hidden', !msg);
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
