/**
 * SourceTypeDetailView - Full-page view/edit component for Source Types
 * Mirrors AnalysisTypeDetailView pattern; replaces legacy side panel usage.
 * Shows all SourceType fields with clean grouping and edit capability.
 */
export class SourceTypeDetailView {
  constructor(api, eventBus, toast) {
    this.api = api;
    this.eventBus = eventBus;
    this.toast = toast;
    this.type = null;
    this.isEditing = false;
    this.isLoading = false;
    this.error = null;
  }

  renderSkeleton() {
    return `
      <div class="detail-view skeleton">
        <div class="detail-header"><div class="pulse" style="width:240px;height:32px"></div></div>
        <div class="detail-body">
          <div class="pulse" style="height:140px"></div>
          <div class="pulse" style="height:220px"></div>
          <div class="pulse" style="height:240px"></div>
        </div>
      </div>`;
  }

  async load(id) {
    this.isLoading = true;
    this.error = null;
    try {
      this.type = await this.api.getSourceType(id);
    } catch (err) {
      console.error('Failed to load source type', err);
      this.error = err;
    } finally {
      this.isLoading = false;
    }
  }

  refresh(container) {
    if (!container) return;
    const contentArea = container.querySelector('.content-area');
    if (!contentArea) return;
    contentArea.innerHTML = this.render();
    this.attachHandlers(contentArea);
  }

  render() {
    if (this.error) {
      return `<div class="error-state"><h2>Failed to load source type</h2><p>${this.escapeHtml(this.error.message)}</p></div>`;
    }
    if (this.isLoading || !this.type) {
      return this.renderSkeleton();
    }

    const t = this.type;
    const editing = this.isEditing;
    const header = this.renderHeader(t, editing);
    const identity = this.renderIdentitySection(t, editing);
    const extraction = this.renderExtractionHintsSection(t, editing);
    const fields = this.renderFieldsSection(t, editing);
    const instructions = this.renderInstructionsSection(t, editing);
    const template = this.renderTemplateSection(t, editing);
    const embedding = this.renderEmbeddingSection(t);
    const meta = this.renderMetadataSection(t);

    return `
      <div class="detail-view source-type-detail">
        ${header}
        <div class="detail-body">
          ${identity}
          ${extraction}
          ${fields}
          ${instructions}
          ${template}
          ${embedding}
          ${meta}
        </div>
      </div>`;
  }

  renderHeader(t, editing) {
    return `
      <div class="detail-header">
        <div class="detail-header-left">
          <button class="btn btn-icon" data-action="back" title="Back to list">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"></polyline></svg>
          </button>
          <h1>${this.escapeHtml(t.name || '(Unnamed Source Type)')}</h1>
          <span class="badge badge-pill">v${t.version || 1}</span>
        </div>
        <div class="detail-header-actions">
          ${editing ? `
            <button class="btn" data-action="cancel">Cancel</button>
            <button class="btn btn-primary" data-action="save">Save</button>
          ` : `
            <button class="btn" data-action="edit">Edit</button>
            <button class="btn btn-danger" data-action="delete">Delete</button>
          `}
        </div>
      </div>`;
  }

  renderIdentitySection(t, editing) {
    return `
      <section class="detail-section">
        <h2>Identity</h2>
        <div class="field-grid">
          <div class="form-field">
            <label>Name</label>
            ${editing ? `<input type="text" data-field="name" value="${this.escapeAttr(t.name)}" />` : `<div class="readonly-value">${this.escapeHtml(t.name)}</div>`}
          </div>
          <div class="form-field">
            <label>Description</label>
            ${editing ? `<textarea data-field="description" rows="3">${this.escapeAttr(t.description)}</textarea>` : `<div class="readonly-value">${this.escapeHtml(t.description || 'No description')}</div>`}
          </div>
          <div class="form-field">
            <label>Version</label>
            ${editing ? `<input type="number" min="1" data-field="version" value="${t.version || 1}" />` : `<div class="readonly-value">${t.version || 1}</div>`}
          </div>
          <div class="form-field">
            <label>Supports Manual Selection</label>
            ${editing ? `<select data-field="supportsManualSelection"><option value="true" ${t.supportsManualSelection ? 'selected' : ''}>Yes</option><option value="false" ${!t.supportsManualSelection ? 'selected' : ''}>No</option></select>` : `<div class="readonly-value">${t.supportsManualSelection ? 'Yes' : 'No'}</div>`}
          </div>
          <div class="form-field">
            <label>Expected Pages (Min)</label>
            ${editing ? `<input type="number" min="0" data-field="expectedPageCountMin" value="${t.expectedPageCountMin ?? ''}" />` : `<div class="readonly-value">${t.expectedPageCountMin ?? 'n/a'}</div>`}
          </div>
          <div class="form-field">
            <label>Expected Pages (Max)</label>
            ${editing ? `<input type="number" min="0" data-field="expectedPageCountMax" value="${t.expectedPageCountMax ?? ''}" />` : `<div class="readonly-value">${t.expectedPageCountMax ?? 'n/a'}</div>`}
          </div>
        </div>
      </section>`;
  }

  renderExtractionHintsSection(t, editing) {
    return `
      <section class="detail-section">
        <h2>Extraction Hints</h2>
        <div class="field-grid">
          <div class="form-field">
            <label>Tags</label>
            ${editing ? `<input type="text" data-field="tags" value="${this.escapeAttr((t.tags || []).join(', '))}" placeholder="tag1, tag2" />` : this.renderTagList(t.tags)}
          </div>
          <div class="form-field">
            <label>Descriptor Hints</label>
            ${editing ? `<textarea data-field="descriptorHints" rows="2" placeholder="One per line">${this.escapeAttr((t.descriptorHints || []).join('\n'))}</textarea>` : this.renderListBlock(t.descriptorHints, 'No descriptor hints')}
          </div>
          <div class="form-field">
            <label>Signal Phrases</label>
            ${editing ? `<textarea data-field="signalPhrases" rows="2" placeholder="One per line">${this.escapeAttr((t.signalPhrases || []).join('\n'))}</textarea>` : this.renderListBlock(t.signalPhrases, 'No signal phrases')}
          </div>
          <div class="form-field">
            <label>Mime Types</label>
            ${editing ? `<input type="text" data-field="mimeTypes" value="${this.escapeAttr((t.mimeTypes || []).join(', '))}" placeholder="application/pdf, text/plain" />` : this.renderListInline(t.mimeTypes)}
          </div>
        </div>
      </section>`;
  }

  renderFieldsSection(t, editing) {
    const fieldQueries = t.fieldQueries || {};
    const entries = Object.entries(fieldQueries);
    return `
      <section class="detail-section">
        <h2>Field Queries <span class="badge">${entries.length}</span></h2>
        ${editing ? `<div class="form-field"><textarea data-field="fieldQueries" rows="6" placeholder="name=query syntax">${this.escapeAttr(entries.map(([k,v]) => `${k}=${v}`).join('\n'))}</textarea></div>`
          : (entries.length === 0 ? '<div class="empty-block">No field queries</div>' : `<table class="kv-table">${entries.map(([k,v]) => `<tr><th>${this.escapeHtml(k)}</th><td>${this.escapeHtml(v)}</td></tr>`).join('')}</table>`)}
      </section>`;
  }

  renderInstructionsSection(t, editing) {
    return `
      <section class="detail-section">
        <h2>Instructions</h2>
        ${editing ? `<textarea data-field="instructions" rows="10" placeholder="Authoring instructions for extraction">${this.escapeAttr(t.instructions || '')}</textarea>` : `<pre class="instructions-block">${this.escapeHtml(t.instructions || 'No instructions')}</pre>`}
      </section>`;
  }

  renderTemplateSection(t, editing) {
    return `
      <section class="detail-section">
        <h2>Output Template</h2>
        ${editing ? `<textarea data-field="outputTemplate" rows="8" placeholder="Template describing output fields">${this.escapeAttr(t.outputTemplate || '')}</textarea>` : `<pre class="template-block">${this.escapeHtml(t.outputTemplate || 'No template')}</pre>`}
      </section>`;
  }

  renderEmbeddingSection(t) {
    return `
      <section class="detail-section">
        <h2>Embedding</h2>
        <div class="kv-grid">
          <div><span class="kv-label">Has Embedding</span><span class="kv-value">${t.typeEmbedding ? 'Yes' : 'No'}</span></div>
          <div><span class="kv-label">Embedding Version</span><span class="kv-value">${t.typeEmbeddingVersion || 0}</span></div>
          <div><span class="kv-label">Embedding Hash</span><span class="kv-value">${this.escapeHtml(t.typeEmbeddingHash || 'n/a')}</span></div>
          <div><span class="kv-label">Computed At</span><span class="kv-value">${this.formatDate(t.typeEmbeddingComputedAt)}</span></div>
        </div>
      </section>`;
  }

  renderMetadataSection(t) {
    return `
      <section class="detail-section">
        <h2>Metadata</h2>
        <div class="kv-grid">
          <div><span class="kv-label">Created</span><span class="kv-value">${this.formatDate(t.createdAt)}</span></div>
          <div><span class="kv-label">Updated</span><span class="kv-value">${this.formatDate(t.updatedAt)}</span></div>
          <div><span class="kv-label">Instructions Updated</span><span class="kv-value">${this.formatDate(t.instructionsUpdatedAt)}</span></div>
          <div><span class="kv-label">Template Updated</span><span class="kv-value">${this.formatDate(t.outputTemplateUpdatedAt)}</span></div>
        </div>
      </section>`;
  }

  collectEdits() {
    const edits = {};
    const getVal = (sel) => {
      const el = document.querySelector(sel);
      return el ? el.value : undefined;
    };
    edits.name = getVal('[data-field="name"]');
    edits.description = getVal('[data-field="description"]');
    const versionVal = getVal('[data-field="version"]');
    if (versionVal) edits.version = parseInt(versionVal, 10);
    const smsVal = getVal('[data-field="supportsManualSelection"]');
    if (smsVal) edits.supportsManualSelection = smsVal === 'true';
    const minVal = getVal('[data-field="expectedPageCountMin"]');
    edits.expectedPageCountMin = minVal === '' ? null : parseInt(minVal, 10);
    const maxVal = getVal('[data-field="expectedPageCountMax"]');
    edits.expectedPageCountMax = maxVal === '' ? null : parseInt(maxVal, 10);
    const tagsVal = getVal('[data-field="tags"]');
    if (tagsVal !== undefined) edits.tags = tagsVal.split(',').map(s => s.trim()).filter(s => s.length > 0);
    const dhVal = getVal('[data-field="descriptorHints"]');
    if (dhVal !== undefined) edits.descriptorHints = dhVal.split('\n').map(s => s.trim()).filter(s => s.length > 0);
    const spVal = getVal('[data-field="signalPhrases"]');
    if (spVal !== undefined) edits.signalPhrases = spVal.split('\n').map(s => s.trim()).filter(s => s.length > 0);
    const mtVal = getVal('[data-field="mimeTypes"]');
    if (mtVal !== undefined) edits.mimeTypes = mtVal.split(',').map(s => s.trim()).filter(s => s.length > 0);
    const fqVal = getVal('[data-field="fieldQueries"]');
    if (fqVal !== undefined) {
      const dict = {};
      fqVal.split('\n').map(l => l.trim()).filter(l => l.length > 0).forEach(line => {
        const eqIdx = line.indexOf('=');
        if (eqIdx > 0) {
          const k = line.substring(0, eqIdx).trim();
          const v = line.substring(eqIdx + 1).trim();
          if (k) dict[k] = v;
        }
      });
      edits.fieldQueries = dict;
    }
    const instrVal = getVal('[data-field="instructions"]');
    if (instrVal !== undefined) edits.instructions = instrVal;
    const tmplVal = getVal('[data-field="outputTemplate"]');
    if (tmplVal !== undefined) edits.outputTemplate = tmplVal;
    return edits;
  }

  attachHandlers(root) {
    if (!root) return;
    const backBtn = root.querySelector('[data-action="back"]');
    if (backBtn) backBtn.addEventListener('click', () => {
      this.eventBus.emit('navigate', 'source-types');
    });
    const editBtn = root.querySelector('[data-action="edit"]');
    if (editBtn) editBtn.addEventListener('click', () => {
      this.isEditing = true;
      this.refresh(root.closest('.app-content'));
    });
    const cancelBtn = root.querySelector('[data-action="cancel"]');
    if (cancelBtn) cancelBtn.addEventListener('click', () => {
      this.isEditing = false;
      this.refresh(root.closest('.app-content'));
    });
    const saveBtn = root.querySelector('[data-action="save"]');
    if (saveBtn) saveBtn.addEventListener('click', async () => {
      await this.handleSave();
    });
    const deleteBtn = root.querySelector('[data-action="delete"]');
    if (deleteBtn) deleteBtn.addEventListener('click', async () => {
      await this.handleDelete();
    });
    // Keyboard shortcut Ctrl+S
    root.addEventListener('keydown', (e) => {
      if (this.isEditing && (e.ctrlKey || e.metaKey) && e.key === 's') {
        e.preventDefault();
        this.handleSave();
      }
    });
  }

  async handleSave() {
    if (!this.type) return;
    try {
      const edits = this.collectEdits();
      await this.api.updateSourceType(this.type.id, edits);
      this.toast.success('Source type updated');
      // Reload entity to reflect patch server-side computed fields
      await this.load(this.type.id);
      this.isEditing = false;
      this.refresh(document.querySelector('.app-content'));
    } catch (err) {
      console.error('Failed to save source type', err);
      this.toast.error('Failed to save source type');
    }
  }

  async handleDelete() {
    if (!this.type) return;
    if (!confirm('Delete this source type? This cannot be undone.')) return;
    try {
      await this.api.deleteSourceType(this.type.id);
      this.toast.success('Source type deleted');
      this.eventBus.emit('navigate', 'source-types');
    } catch (err) {
      console.error('Failed to delete source type', err);
      this.toast.error('Failed to delete source type');
    }
  }

  renderTagList(tags) {
    if (!tags || tags.length === 0) return '<div class="empty-block">No tags</div>';
    return `<div class="tag-list">${tags.map(t => `<span class="tag">${this.escapeHtml(t)}</span>`).join('')}</div>`;
  }

  renderListBlock(list, emptyMsg) {
    if (!list || list.length === 0) return `<div class="empty-block">${this.escapeHtml(emptyMsg)}</div>`;
    return `<ul class="list-block">${list.map(i => `<li>${this.escapeHtml(i)}</li>`).join('')}</ul>`;
  }

  renderListInline(list) {
    if (!list || list.length === 0) return '<div class="empty-block">None</div>';
    return `<div class="inline-list">${list.map(i => `<span class="inline-item">${this.escapeHtml(i)}</span>`).join('')}</div>`;
  }

  formatDate(dateVal) {
    if (!dateVal) return 'n/a';
    const dt = new Date(dateVal);
    if (isNaN(dt.getTime())) return 'invalid';
    return dt.toLocaleString();
  }

  escapeHtml(text) {
    if (text == null) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }

  escapeAttr(text) {
    return this.escapeHtml(text);
  }
}
