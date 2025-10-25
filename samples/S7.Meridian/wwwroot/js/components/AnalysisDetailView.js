/**
 * AnalysisDetailView - Full-page detail view for a single Analysis (Pipeline)
 * Handles create, view, and edit modes for analyses
 */
export class AnalysisDetailView {
  constructor(api, eventBus, router, toast) {
    this.api = api;
    this.eventBus = eventBus;
    this.router = router;
    this.toast = toast;
    this.analysis = null;
    this.analysisTypes = [];
    this.isEditing = false;
    this.isCreating = false;
    this.filesToUpload = [];
    this.documents = [];
    this.deliverable = null;
    this.deliverableError = null;
  }

  /**
   * Initialize for create mode
   */
  async initCreate() {
    this.isCreating = true;
    this.isEditing = true;
    this.analysis = {
      name: '',
      description: '',
      analysisTypeId: null,
      notes: ''
    };
    await this.loadAnalysisTypes();
    this.documents = [];
    this.deliverable = null;
    this.deliverableError = null;
  }

  /**
   * Load existing analysis
   */
  async load(id) {
    try {
      this.analysis = await this.api.getPipeline(id);
      await this.loadNotes(id);
      await this.loadDocuments(id);
      await this.loadDeliverable(id);
    } catch (err) {
      console.error('Failed to load analysis', err);
      this.toast.error('Failed to load analysis');
      this.analysis = null;
      this.documents = [];
      this.deliverable = null;
      this.deliverableError = null;
    }
  }

  /**
   * Load available analysis types for dropdown
   */
  async loadAnalysisTypes() {
    try {
      this.analysisTypes = await this.api.getAnalysisTypes();
    } catch (err) {
      console.error('Failed to load analysis types', err);
      this.analysisTypes = [];
    }
  }

  renderSkeleton() {
    return `<div class="detail-view loading"><p>Loading analysis...</p></div>`;
  }

  render() {
    if (!this.isCreating && !this.analysis) {
      return `
        <div class="detail-view error">
          <h2>Analysis Not Found</h2>
          <p>The requested analysis could not be loaded.</p>
          <button class="btn" data-nav="back">Back</button>
        </div>
      `;
    }

    return `
      <div class="detail-view analysis-detail">
        ${this.renderHeader()}
        <div class="detail-sections">
          ${this.renderIdentitySection()}
          ${this.renderNotesSection()}
          ${this.renderDeliverableSection()}
          ${this.renderDocumentsSection()}
          ${this.isCreating || this.isEditing ? this.renderFileUploadSection() : ''}
        </div>
      </div>
    `;
  }

  renderHeader() {
    const title = this.isCreating ? 'New Analysis' : (this.analysis.name || 'Untitled');
    const subtitle = this.isCreating
      ? 'Create a new analysis pipeline'
      : (this.analysis.description || 'No description provided.');

    return `
      <div class="page-header">
        <div class="breadcrumbs">
          <a href="#/analyses" class="breadcrumb">Analyses</a>
          <span class="breadcrumb-sep">›</span>
          <span class="breadcrumb current">${this.escape(title)}</span>
        </div>
        <div class="page-header-main">
          <h1>${this.escape(title)}</h1>
          <p class="subtitle">${this.escape(subtitle)}</p>
        </div>
        <div class="page-header-actions">
          ${this.renderActions()}
        </div>
        ${!this.isCreating ? `
          <div class="meta-line">
            <span class="meta-item">ID: ${this.escape(this.analysis.id || this.analysis.Id || 'N/A')}</span>
            <span class="meta-item">Status: ${this.escape(this.analysis.status || 'Active')}</span>
          </div>
        ` : ''}
      </div>
    `;
  }

  renderActions() {
    if (this.isCreating) {
      return `
        <button class="btn btn-secondary" data-action="cancel">Cancel</button>
        <button class="btn btn-primary" data-action="save-create">Create Analysis</button>
      `;
    }

    if (this.isEditing) {
      return `
        <button class="btn btn-secondary" data-action="cancel">Cancel</button>
        <button class="btn btn-primary" data-action="save">Save</button>
      `;
    }

    return `
      <button class="btn btn-primary" data-action="open-workspace">Open Workspace</button>
      <button class="btn btn-secondary" data-action="edit">Edit</button>
      <button class="btn btn-danger" data-action="delete">Delete</button>
      <button class="btn" data-nav="back">Back</button>
    `;
  }

  renderIdentitySection() {
    return `
      <section class="detail-section">
        <h2>Identity</h2>
        <div class="field-grid">
          <div class="form-field">
            <label>Name</label>
            ${this.isCreating || this.isEditing
              ? `<input type="text" name="name" value="${this.escapeAttr(this.analysis.name || '')}" maxlength="128" placeholder="My Analysis" required />`
              : `<div class="readonly-value">${this.escape(this.analysis.name)}</div>`}
          </div>
          <div class="form-field">
            <label>Description</label>
            ${this.isCreating || this.isEditing
              ? `<textarea name="description" rows="3" maxlength="512" placeholder="What this analysis is about...">${this.escape(this.analysis.description || '')}</textarea>`
              : `<div class="readonly-value">${this.escape(this.analysis.description)}</div>`}
          </div>
          ${this.renderAnalysisTypeField()}
        </div>
      </section>
    `;
  }

  renderAnalysisTypeField() {
    if (this.isCreating) {
      // Show dropdown for type selection during creation
      const options = this.analysisTypes.map(type => {
        const selected = this.analysis.analysisTypeId === type.id ? 'selected' : '';
        return `<option value="${this.escapeAttr(type.id)}" ${selected}>${this.escape(type.name)}</option>`;
      }).join('');

      return `
        <div class="form-field">
          <label>Analysis Type</label>
          <select name="analysisTypeId">
            <option value="">Select a type...</option>
            ${options}
          </select>
        </div>
      `;
    }

    if (!this.isEditing && this.analysis.analysisTypeId) {
      // Show readonly link to analysis type in view mode
      return `
        <div class="form-field">
          <label>Analysis Type</label>
          <div class="readonly-value">
            <a href="#/analysis-types/${this.escapeAttr(this.analysis.analysisTypeId)}/view">
              ${this.escape(this.analysis.analysisTypeName || this.analysis.analysisTypeId)}
            </a>
          </div>
        </div>
      `;
    }

    return '';
  }

  renderNotesSection() {
    const notes = this.analysis.notes || '';

    return `
      <section class="detail-section">
        <h2>Authoritative Notes</h2>
        <div class="form-field">
          <label>Notes</label>
          ${this.isCreating || this.isEditing
            ? `<textarea name="notes" rows="10" placeholder="Add authoritative information in natural language...

Example:
PRIMARY CONTACT: Jordan Kim is the VP of Enterprise Solutions
REVENUE: FY2024 revenue was $52.3M USD
EMPLOYEE COUNT: 175 employees as of October 2024">${this.escape(notes)}</textarea>
              <div class="field-help">Use natural language. The system will automatically override document extractions.</div>`
            : `<div class="readonly-value notes-text">${this.escape(notes)}</div>`}
        </div>
      </section>
    `;
  }

  renderDeliverableSection() {
    if (this.isCreating) {
      return '';
    }

    const pipelineId = this.analysis?.id || this.analysis?.Id;
    const hasDeliverable = Boolean(this.deliverable);
    const error = this.deliverableError;

    const actionButtons = [];

    if (hasDeliverable) {
      actionButtons.push('<button class="btn btn-secondary" data-action="download-deliverable">Download Markdown</button>');
    }

    if (pipelineId) {
      actionButtons.push(`<a class="btn" href="#/analyses/${this.escapeAttr(pipelineId)}" data-action="open-workspace">Open in Workspace</a>`);
    }

    const actions = actionButtons.length
      ? `
        <div class="deliverable-actions">
          ${actionButtons.join('\n          ')}
        </div>
      `
      : '';

    let body = '';

    if (error) {
      body = `
        <div class="deliverable-empty">
          <strong>Unable to load deliverable.</strong>
          <span>Please try again from the workspace. (${this.escape(error.message || 'Unknown error')})</span>
        </div>
      `;
    } else if (!hasDeliverable) {
      body = `
        <div class="deliverable-empty">
          No deliverable is available yet. Run the pipeline from the workspace to generate one.
        </div>
      `;
    } else {
      const deliverable = this.deliverable;
      const docCount = Array.isArray(deliverable.sourceDocumentIds) ? deliverable.sourceDocumentIds.length : 0;
      const quality = deliverable.quality || {};
      const coverage = quality.citationCoverage != null ? `${quality.citationCoverage}% citation coverage` : null;
      const confidence = quality.highConfidence != null ? `${quality.highConfidence} high-confidence facts` : null;

      const metadataItems = [];
      if (deliverable.deliverableTypeId) {
        metadataItems.push(`Type: ${deliverable.deliverableTypeId}`);
      }
      if (docCount) {
        metadataItems.push(`Documents: ${docCount}`);
      }

      const completedAt = this.formatTimestamp(deliverable.completedAt);
      if (completedAt) {
        metadataItems.push(`Completed: ${completedAt}`);
      }

      const updatedAt = this.formatTimestamp(deliverable.updatedAt);
      if (updatedAt && updatedAt !== completedAt) {
        metadataItems.push(`Updated: ${updatedAt}`);
      }

      if (coverage) {
        metadataItems.push(coverage);
      }

      if (confidence) {
        metadataItems.push(confidence);
      }

      const markdown = deliverable.renderedMarkdown || '';
      const preview = this.buildDeliverablePreview(markdown);

      body = `
        ${metadataItems.length ? `
          <div class="deliverable-meta">
            ${metadataItems.map(item => `<span class="deliverable-meta-item">${this.escape(item)}</span>`).join('')}
          </div>
        ` : ''}
        <div class="deliverable-preview">${this.escape(preview.text)}</div>
        ${preview.remainingLines ? `<div class="deliverable-preview-footer">… ${preview.remainingLines} more line(s) in workspace</div>` : ''}
      `;
    }

    return `
      <section class="detail-section">
        <h2>Latest Deliverable</h2>
        ${actions}
        ${body}
      </section>
    `;
  }

  renderDocumentsSection() {
    const docs = Array.isArray(this.documents) ? this.documents : [];
    const hasDocs = docs.length > 0;

    if (this.isCreating) {
      return `
        <section class="detail-section">
          <h2>Documents</h2>
          <div class="document-empty-state">Documents will appear after the analysis is created. Selected uploads are queued below.</div>
        </section>
      `;
    }

    const list = hasDocs
      ? `
        <div class="documents-list">
          ${docs.map(doc => this.renderDocumentItem(doc)).join('')}
        </div>
      `
      : `<div class="document-empty-state">No documents uploaded yet.</div>`;

    return `
      <section class="detail-section">
        <h2>Documents${hasDocs ? ` <span class="section-count">(${docs.length})</span>` : ''}</h2>
        ${list}
      </section>
    `;
  }

  renderDocumentItem(doc) {
    const fileName = doc.originalFileName || doc.OriginalFileName || doc.fileName || 'Untitled Document';
    const statusValue = doc.status || doc.Status || 'Unknown';
    const sizeBytes = doc.size || doc.Size || 0;
    const sourceType = doc.sourceTypeName || doc.SourceTypeName || doc.sourceType || doc.SourceType || 'Unclassified';
    const isVirtual = doc.isVirtual || doc.IsVirtual || false;
    const confidenceValue = doc.classificationConfidence || doc.ClassificationConfidence;
    const status = typeof statusValue === 'string' ? statusValue : this.mapDocumentStatus(statusValue);
    const confidenceText = this.formatConfidence(confidenceValue);
    const confidence = confidenceText ? `${confidenceText} confidence` : '';
    const uploadedAt = doc.uploadedAt || doc.UploadedAt || doc.createdAt || doc.CreatedAt;
    const statusKey = typeof status === 'string'
      ? status.toLowerCase().replace(/[^a-z0-9]+/g, '-')
      : 'unknown';

    return `
      <div class="document-item" data-document-id="${this.escapeAttr(doc.id || doc.Id || '')}">
        <div class="document-icon">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
            <polyline points="14 2 14 8 20 8"></polyline>
          </svg>
        </div>
        <div class="document-info">
          <div class="document-name">${this.escape(fileName)}</div>
          <div class="document-meta">
            <span class="document-status status-${this.escapeAttr(statusKey)}">${this.escape(status)}</span>
            ${sizeBytes ? `<span class="document-size">${this.escape(this.formatFileSize(sizeBytes))}</span>` : ''}
            ${sourceType ? `<span class="document-type">${this.escape(sourceType)}</span>` : ''}
            ${isVirtual ? '<span class="document-badge">Virtual</span>' : ''}
            ${confidence ? `<span class="document-confidence">${this.escape(confidence)}</span>` : ''}
            ${uploadedAt ? `<span class="document-uploaded">${this.escape(this.formatTimestamp(uploadedAt))}</span>` : ''}
          </div>
        </div>
      </div>
    `;
  }

  renderFileUploadSection() {
    return `
      <section class="detail-section">
        <h2>Upload Documents</h2>
        <div class="form-field">
          <label>Upload Files ${this.isCreating ? '(optional)' : ''}</label>
          <input type="file" name="files" multiple accept=".pdf,.txt,.docx,.doc" data-action="select-files" />
          <div class="field-help">Upload documents to be analyzed (PDF, TXT, DOCX supported)</div>
          <div class="selected-files-list" style="display: none; margin-top: 12px;"></div>
        </div>
      </section>
    `;
  }

  async loadNotes(id) {
    try {
      const notesData = await this.api.getNotes(id);
      this.analysis.notes = notesData?.authoritativeNotes || '';
    } catch (error) {
      this.analysis.notes = '';
    }
  }

  async loadDocuments(id) {
    try {
      const docs = await this.api.getDocuments(id);
      this.documents = Array.isArray(docs) ? docs : [];
    } catch (error) {
      console.warn('Failed to load documents', error);
      this.documents = [];
    }
  }

  async loadDeliverable(id) {
    try {
      const deliverable = await this.api.getDeliverable(id);
      this.deliverable = deliverable || null;
      this.deliverableError = null;
    } catch (error) {
      if (error?.status === 404) {
        this.deliverable = null;
        this.deliverableError = null;
      } else {
        console.warn('Failed to load deliverable', error);
        this.deliverable = null;
        this.deliverableError = error;
      }
    }
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    if (!container) return;

    // Back button
    const backBtn = container.querySelector('[data-nav="back"]');
    if (backBtn) {
      backBtn.addEventListener('click', () => {
        window.history.back();
      });
    }

    // Action buttons
    container.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.preventDefault();
        const action = btn.dataset.action;

        switch (action) {
          case 'save-create':
            await this.handleSaveCreate(container);
            break;
          case 'save':
            await this.handleSave(container);
            break;
          case 'edit':
            this.handleEdit();
            break;
          case 'cancel':
            await this.handleCancel();
            break;
          case 'delete':
            await this.handleDelete();
            break;
          case 'open-workspace':
            this.handleOpenWorkspace();
            break;
          case 'download-deliverable':
            await this.handleDownloadDeliverable();
            break;
          case 'select-files':
            // File input handled by change event
            break;
        }
      });
    });

    // File input
    const fileInput = container.querySelector('input[type="file"]');
    if (fileInput) {
      fileInput.addEventListener('change', (e) => {
        this.filesToUpload = Array.from(e.target.files);
        this.updateFilesList(container);
      });
    }

    // Refresh selected file list if files are already tracked
    this.updateFilesList(container);
  }

  /**
   * Update the files list display
   */
  updateFilesList(container) {
    const filesList = container.querySelector('.selected-files-list');
    if (!filesList) return;

    if (this.filesToUpload.length === 0) {
      filesList.style.display = 'none';
      return;
    }

    filesList.style.display = 'block';
    filesList.innerHTML = `
      <strong>Selected files (${this.filesToUpload.length}):</strong>
      <ul style="margin: 8px 0 0 20px; list-style: disc;">
        ${this.filesToUpload.map(f => `<li>${this.escape(f.name)} (${this.escape(this.formatFileSize(f.size))})</li>`).join('')}
      </ul>
    `;
  }

  /**
   * Handle save for create mode
   */
  async handleSaveCreate(container) {
    const formData = this.collectFormData(container);

    if (!formData.name?.trim()) {
      this.toast.error('Please enter a name for the analysis');
      return;
    }

    try {
      // Create pipeline
      const pipeline = await this.api.createPipeline({
        name: formData.name,
        description: formData.description || '',
        analysisTypeId: formData.analysisTypeId || null
      });

      const pipelineId = pipeline.id || pipeline.Id;
      this.toast.success(`Analysis "${formData.name}" created`);

      // Set notes if provided
      if (formData.notes?.trim()) {
        try {
          await this.api.setNotes(pipelineId, formData.notes, false);
        } catch (e) {
          console.error('Failed to set notes:', e);
          // Don't fail the whole operation if notes fail
        }
      }

      // Upload files if provided
      if (this.filesToUpload.length > 0) {
        try {
          for (const file of this.filesToUpload) {
            await this.api.uploadDocument(pipelineId, file);
          }
          this.toast.success(`${this.filesToUpload.length} file(s) uploaded`);
        } catch (e) {
          console.error('Failed to upload files:', e);
          this.toast.error('Failed to upload some files');
        }
      }

      // Navigate to workspace
      this.eventBus.emit('navigate', 'analysis-workspace', { pipelineId });

    } catch (error) {
      console.error('Failed to create analysis:', error);
      this.toast.error('Failed to create analysis');
    }
  }

  /**
   * Handle save for edit mode
   */
  async handleSave(container) {
    const formData = this.collectFormData(container);

    if (!formData.name?.trim()) {
      this.toast.error('Please enter a name for the analysis');
      return;
    }

    try {
      // Update notes
      if (formData.notes !== this.analysis.notes) {
        await this.api.setNotes(this.analysis.id || this.analysis.Id, formData.notes, false);
      }

      // Upload new files if provided
      if (this.filesToUpload.length > 0) {
        const pipelineId = this.analysis.id || this.analysis.Id;
        for (const file of this.filesToUpload) {
          await this.api.uploadDocument(pipelineId, file);
        }
        this.toast.success(`${this.filesToUpload.length} file(s) uploaded`);
      }

      this.toast.success('Analysis updated');

      // Reload and switch to view mode
      await this.load(this.analysis.id || this.analysis.Id);
      this.isEditing = false;
      this.filesToUpload = [];

      // Refresh the view
      const appContainer = document.querySelector('#app');
      if (appContainer) {
        this.eventBus.emit('navigate', 'analysis-view', { id: this.analysis.id || this.analysis.Id });
      }

    } catch (error) {
      console.error('Failed to update analysis:', error);
      this.toast.error('Failed to update analysis');
    }
  }

  /**
   * Handle edit button
   */
  handleEdit() {
    this.isEditing = true;
    this.eventBus.emit('navigate', 'analysis-edit', { id: this.analysis.id || this.analysis.Id });
  }

  /**
   * Handle cancel button
   */
  async handleCancel() {
    if (this.isCreating) {
      this.eventBus.emit('navigate', 'analyses-list');
    } else {
      this.isEditing = false;
      this.filesToUpload = [];
      this.eventBus.emit('navigate', 'analysis-view', { id: this.analysis.id || this.analysis.Id });
    }
  }

  /**
   * Handle delete button
   */
  async handleDelete() {
    const name = this.analysis.name || 'this analysis';
    const confirmed = confirm(`Are you sure you want to delete "${name}"? This action cannot be undone.`);
    if (!confirmed) return;

    try {
      await this.api.deletePipeline(this.analysis.id || this.analysis.Id);
      this.toast.success('Analysis deleted');
      this.eventBus.emit('navigate', 'analyses-list');
    } catch (error) {
      console.error('Failed to delete analysis:', error);
      this.toast.error('Failed to delete analysis');
    }
  }

  /**
   * Handle open workspace button
   */
  handleOpenWorkspace() {
    this.eventBus.emit('navigate', 'analysis-workspace', {
      pipelineId: this.analysis.id || this.analysis.Id
    });
  }

  async handleDownloadDeliverable() {
    const pipelineId = this.analysis?.id || this.analysis?.Id;
    if (!pipelineId) {
      this.toast.error('Analysis ID is not available');
      return;
    }

    try {
      const markdown = await this.api.getDeliverableMarkdown(pipelineId);
      if (!markdown) {
        this.toast.info('Deliverable content is empty');
        return;
      }

      const blob = new Blob([markdown], { type: 'text/markdown' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = this.buildDeliverableFileName(pipelineId);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);

      this.toast.success('Deliverable downloaded');
    } catch (error) {
      console.error('Failed to download deliverable:', error);
      this.toast.error('Failed to download deliverable');
    }
  }

  /**
   * Collect form data from container
   */
  collectFormData(container) {
    const data = {};
    container.querySelectorAll('input[name], textarea[name], select[name]').forEach(field => {
      data[field.name] = field.value;
    });
    return data;
  }

  /**
   * Format file size for display
   */
  formatFileSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }

  mapDocumentStatus(statusValue) {
    const statusMap = {
      0: 'Pending',
      1: 'Extracted',
      2: 'Indexed',
      3: 'Classified',
      4: 'Failed',
      Pending: 'Pending',
      Extracted: 'Extracted',
      Indexed: 'Indexed',
      Classified: 'Classified',
      Failed: 'Failed'
    };

    return statusMap[statusValue] || statusValue || 'Unknown';
  }

  formatConfidence(value) {
    if (value == null || value === '') {
      return '';
    }

    const numeric = Number(value);
    if (!Number.isFinite(numeric)) {
      return String(value);
    }

    if (numeric <= 1) {
      return `${Math.round(numeric * 100)}%`;
    }

    return `${Math.round(numeric)}%`;
  }

  formatTimestamp(value) {
    if (!value) {
      return '';
    }

    try {
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) {
        return String(value);
      }

      return date.toLocaleString();
    } catch (error) {
      return String(value);
    }
  }

  buildDeliverablePreview(markdown) {
    if (!markdown) {
      return {
        text: 'Deliverable preview not available.',
        remainingLines: 0
      };
    }

    const lines = markdown.split('\n');
    const limit = 32;
    const previewLines = lines.slice(0, limit);
    const remainingLines = Math.max(lines.length - previewLines.length, 0);

    return {
      text: previewLines.join('\n'),
      remainingLines
    };
  }

  buildDeliverableFileName(pipelineId) {
    const name = this.analysis?.name || 'analysis';
    const safeName = name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || `analysis-${pipelineId}`;

    return `${safeName}-deliverable.md`;
  }

  /**
   * Escape HTML to prevent XSS
   */
  escape(text) {
    if (text == null) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }

  /**
   * Escape HTML attribute values
   */
  escapeAttr(text) {
    if (text == null) return '';
    return String(text)
      .replace(/&/g, '&amp;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
  }
}
