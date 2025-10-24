/**
 * AnalysisTypeDetailView - Full-page detail view for a single Analysis Type
 * Replaces side panel usage; shows all fields with edit workflow.
 */
export class AnalysisTypeDetailView {
  constructor(api, eventBus, router, toast) {
    this.api = api;
    this.eventBus = eventBus;
    this.router = router;
    this.toast = toast;
    this.type = null;
    this.isEditing = false;
  }

  async load(id) {
    try {
      this.type = await this.api.getAnalysisType(id);
    } catch (err) {
      console.error('Failed to load analysis type', err);
      this.toast.error('Failed to load analysis type');
      this.type = null;
    }
  }

  renderSkeleton() {
    return `<div class="detail-view loading"><p>Loading analysis type...</p></div>`;
  }

  render() {
    if (!this.type) {
      return `<div class="detail-view error"><h2>Analysis Type Not Found</h2><p>The requested analysis type could not be loaded.</p><button class="btn" data-nav="back">Back</button></div>`;
    }

    const schemaObj = this.safeParseJson(this.type.jsonSchema);
    const fieldCount = schemaObj && schemaObj.properties ? Object.keys(schemaObj.properties).length : 0;

    return `
      <div class="detail-view analysis-type-detail">
        ${this.renderHeader(fieldCount)}
        <div class="detail-sections">
          ${this.renderIdentitySection()}
          ${this.renderTemplateSection()}
          ${this.renderInstructionsSection()}
          ${this.renderSchemaSection(fieldCount)}
          ${this.renderTagsDescriptorsSection()}
          ${this.renderTimestampsSection()}
        </div>
      </div>
    `;
  }

  renderHeader(fieldCount) {
    return `
      <div class="page-header">
        <div class="breadcrumbs">
          <a href="#/analysis-types" class="breadcrumb">Analysis Types</a>
          <span class="breadcrumb-sep">›</span>
          <span class="breadcrumb current">${this.escape(this.type.name || 'Untitled')}</span>
        </div>
        <div class="page-header-main">
          <h1>${this.escape(this.type.name || 'Untitled')}</h1>
          <p class="subtitle">${this.escape(this.type.description || 'No description provided.')}</p>
        </div>
        <div class="page-header-actions">
          ${this.isEditing
            ? `<button class="btn btn-secondary" data-action="cancel">Cancel</button>
               <button class="btn btn-primary" data-action="save">Save</button>`
            : `<button class="btn btn-secondary" data-action="edit">Edit</button>
               <button class="btn btn-danger" data-action="delete">Delete</button>`}
          <button class="btn" data-nav="back">Back</button>
        </div>
        <div class="meta-line">
          <span class="meta-item">Fields: ${fieldCount}</span>
          <span class="meta-item">ID: ${this.escape(this.type.id)}</span>
          <span class="meta-item">Version: ${this.escape(this.type.version ?? 1)}</span>
        </div>
      </div>
    `;
  }

  renderIdentitySection() {
    return `
      <section class="detail-section">
        <h2>Identity</h2>
        <div class="field-grid">
          <div class="form-field">
            <label>Name</label>
            ${this.isEditing
              ? `<input type="text" name="name" value="${this.escapeAttr(this.type.name)}" maxlength="128" />`
              : `<div class="readonly-value">${this.escape(this.type.name)}</div>`}
          </div>
          <div class="form-field">
            <label>Description</label>
            ${this.isEditing
              ? `<textarea name="description" rows="3" maxlength="512">${this.escape(this.type.description)}</textarea>`
              : `<div class="readonly-value">${this.escape(this.type.description)}</div>`}
          </div>
        </div>
      </section>
    `;
  }

  renderTemplateSection() {
    const template = this.type.outputTemplate || '';
    return `
      <section class="detail-section">
        <h2>Template</h2>
        <div class="form-field">
          ${this.isEditing
            ? `<textarea name="outputTemplate" rows="14" class="mono">${this.escape(template)}</textarea>`
            : `<div class="readonly-value"><pre class="template-preview"><code>${this.escape(template)}</code></pre></div>`}
        </div>
      </section>
    `;
  }

  renderInstructionsSection() {
    const instr = this.type.instructions || '';
    return `
      <section class="detail-section">
        <h2>Instructions</h2>
        <div class="form-field">
          ${this.isEditing
            ? `<textarea name="instructions" rows="12">${this.escape(instr)}</textarea>`
            : `<div class="readonly-value instructions-text">${this.escape(instr)}</div>`}
        </div>
      </section>
    `;
  }

  renderSchemaSection(fieldCount) {
    const schema = this.type.jsonSchema || '';
    return `
      <section class="detail-section">
        <h2>JSON Schema (${fieldCount} field${fieldCount === 1 ? '' : 's'})</h2>
        <div class="form-field">
          ${this.isEditing
            ? `<textarea name="jsonSchema" rows="10" class="mono">${this.escape(schema)}</textarea>`
            : `<div class="readonly-value"><pre class="schema-preview"><code>${this.escape(schema)}</code></pre></div>`}
        </div>
      </section>
    `;
  }

  renderTagsDescriptorsSection() {
    const tags = this.type.tags || [];
    const descs = this.type.descriptors || [];
    return `
      <section class="detail-section">
        <div class="field-grid cols-2">
          <div class="form-field">
            <label>Tags</label>
            ${this.isEditing
              ? `<input name="tags" value="${this.escapeAttr(tags.join(', '))}" placeholder="Comma-separated" />`
              : `<div class="readonly-value">${tags.length ? `<ul class="chip-list">${tags.map(t => `<li class="chip">${this.escape(t)}</li>`).join('')}</ul>` : '<p class="empty">None</p>'}</div>`}
          </div>
          <div class="form-field">
            <label>Descriptors</label>
            ${this.isEditing
              ? `<input name="descriptors" value="${this.escapeAttr(descs.join(', '))}" placeholder="Comma-separated" />`
              : `<div class="readonly-value">${descs.length ? `<ul class="chip-list">${descs.map(d => `<li class="chip">${this.escape(d)}</li>`).join('')}</ul>` : '<p class="empty">None</p>'}</div>`}
          </div>
        </div>
      </section>
    `;
  }

  renderTimestampsSection() {
    return `
      <section class="detail-section meta">
        <h2>Metadata</h2>
        <div class="kv compact">
          <div class="kv-row"><div class="kv-key">Created</div><div class="kv-val">${this.formatDate(this.type.createdAt)}</div></div>
          <div class="kv-row"><div class="kv-key">Updated</div><div class="kv-val">${this.formatDate(this.type.updatedAt)}</div></div>
        </div>
      </section>
    `;
  }

  collectEdits() {
    const root = document.querySelector('.analysis-type-detail');
    if (!root) return {};
    const get = (sel) => root.querySelector(sel);
    const val = (el) => el ? el.value.trim() : '';
    const csv = (v) => v.split(',').map(s => s.trim()).filter(Boolean);
    return {
      name: val(get('input[name="name"]')) || this.type.name,
      description: val(get('textarea[name="description"]')) || this.type.description,
      outputTemplate: val(get('textarea[name="outputTemplate"]')) || this.type.outputTemplate,
      instructions: val(get('textarea[name="instructions"]')) || this.type.instructions,
      jsonSchema: val(get('textarea[name="jsonSchema"]')) || this.type.jsonSchema,
      tags: csv(val(get('input[name="tags"]')) || this.type.tags.join(', ')),
      descriptors: csv(val(get('input[name="descriptors"]')) || this.type.descriptors.join(', '))
    };
  }

  attachHandlers(container) {
    const root = container.querySelector('.analysis-type-detail');
    if (!root) return;

    root.addEventListener('click', async (e) => {
      const actionBtn = e.target.closest('[data-action]');
      const navBtn = e.target.closest('[data-nav]');
      if (navBtn) {
        const nav = navBtn.getAttribute('data-nav');
        if (nav === 'back') {
          this.router.navigate('#/analysis-types');
        }
      }
      if (!actionBtn) return;
      const action = actionBtn.getAttribute('data-action');
      switch (action) {
        case 'edit':
          this.isEditing = true;
          this.refresh(container);
          break;
        case 'cancel':
          this.isEditing = false;
          this.refresh(container);
          break;
        case 'save':
          await this.handleSave(container);
          break;
        case 'delete':
          await this.handleDelete();
          break;
      }
    });

    // Keyboard shortcut Ctrl+S to save in edit mode
    document.addEventListener('keydown', this.boundKeyHandler = (evt) => {
      if (this.isEditing && (evt.ctrlKey || evt.metaKey) && evt.key.toLowerCase() === 's') {
        evt.preventDefault();
        this.handleSave(container);
      }
    });
  }

  async handleSave(container) {
    try {
      const updates = this.collectEdits();
      await this.api.updateAnalysisType(this.type.id, updates);
      this.toast.success('Saved');
      // Reload fresh
      await this.load(this.type.id);
      this.isEditing = false;
      this.refresh(container);
    } catch (err) {
      console.error('Save failed', err);
      this.toast.error('Save failed');
    }
  }

  async handleDelete() {
    const confirmed = window.confirm(`Delete analysis type "${this.type.name}"? This cannot be undone.`);
    if (!confirmed) return;
    try {
      await this.api.deleteAnalysisType(this.type.id);
      this.toast.success('Analysis type deleted');
      this.router.navigate('#/analysis-types');
    } catch (err) {
      console.error('Delete failed', err);
      this.toast.error('Delete failed');
    }
  }

  refresh(container) {
    const contentArea = container.querySelector('.content-area');
    if (!contentArea) return;
    contentArea.innerHTML = this.render();
    this.attachHandlers(container);
  }

  safeParseJson(raw) {
    if (!raw) return null;
    try {
      return JSON.parse(raw);
    } catch { return null; }
  }

  formatDate(dt) {
    if (!dt) return '—';
    const date = new Date(dt);
    if (isNaN(date.getTime())) return 'Invalid';
    return date.toLocaleString();
  }

  escape(str) {
    if (str == null) return '';
    const div = document.createElement('div');
    div.textContent = String(str);
    return div.innerHTML;
  }
  escapeAttr(str) { return this.escape(str).replace(/"/g, '&quot;'); }
}
