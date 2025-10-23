/**
 * TypeFormView - Shared full-page form component for CRUD operations
 * Supports both Analysis Types and Source Types
 * Three modes: View (read-only), Create (new entity), Edit (modify existing)
 */
export class TypeFormView {
  constructor(mode, entityType, api, eventBus, toast) {
    this.mode = mode; // 'view', 'create', 'edit'
    this.entityType = entityType; // 'analysis', 'source'
    this.api = api;
    this.eventBus = eventBus;
    this.toast = toast;
    this.isDirty = false;
    this.entity = null;
    this.tags = [];
    this.beforeUnloadHandler = null;
  }

  /**
   * Render the full form view
   * @param {string|null} id - Entity ID (null for create mode)
   * @returns {Promise<string>} HTML content
   */
  async render(id = null) {
    // Load entity data based on mode
    if (id && (this.mode === 'view' || this.mode === 'edit')) {
      await this.loadEntity(id);
    } else if (this.mode === 'create') {
      await this.loadTemplate();
    }

    return `
      <div class="type-form-view mode-${this.mode}">
        ${this.renderHeader()}
        ${this.renderContent()}
        ${this.renderFooter()}
      </div>
    `;
  }

  /**
   * Render header with mode badge and title
   */
  renderHeader() {
    const modeLabels = {
      view: 'View',
      create: 'Create',
      edit: 'Edit'
    };

    const typeLabel = this.entityType === 'analysis' ? 'Analysis Type' : 'Source Type';
    const title = this.entity?.name
      ? `${modeLabels[this.mode]} ${typeLabel}: ${this.entity.name}`
      : `${modeLabels[this.mode]} ${typeLabel}`;

    return `
      <div class="type-form-header">
        <span class="type-form-mode-badge mode-${this.mode}">
          ${modeLabels[this.mode]}
        </span>
        <h1 class="type-form-title">${this.escapeHtml(title)}</h1>
      </div>
    `;
  }

  /**
   * Render main content area with form sections
   */
  renderContent() {
    const isReadonly = this.mode === 'view';
    const templateLabel = this.entityType === 'analysis' ? 'Template' : 'Schema';

    return `
      <div class="type-form-content">
        <div class="type-form-container">

          <!-- Basic Information Section -->
          <div class="type-form-section">
            <div class="type-form-section-header">
              <div class="type-form-section-icon">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                  <polyline points="14 2 14 8 20 8"></polyline>
                  <line x1="16" y1="13" x2="8" y2="13"></line>
                  <line x1="16" y1="17" x2="8" y2="17"></line>
                  <polyline points="10 9 9 9 8 9"></polyline>
                </svg>
              </div>
              <h2 class="type-form-section-title">Basic Information</h2>
            </div>
            <div class="type-form-section-body">

              <!-- Name Field -->
              <div class="type-form-field">
                <label class="type-form-field-label">
                  Name
                  ${!isReadonly ? '<span class="required">*</span>' : ''}
                </label>
                <input
                  type="text"
                  name="name"
                  class="type-form-field-input"
                  value="${this.escapeHtml(this.entity?.name || '')}"
                  ${isReadonly ? 'readonly' : ''}
                  ${!isReadonly ? 'required' : ''}
                  placeholder="${isReadonly ? '' : 'Enter type name...'}"
                />
                ${!isReadonly ? '<p class="type-form-field-help">A descriptive name for this type (e.g., "Financial Analysis", "Invoice Processing").</p>' : ''}
              </div>

              <!-- Description Field -->
              <div class="type-form-field">
                <label class="type-form-field-label">
                  Description
                  ${!isReadonly ? '<span class="required">*</span>' : ''}
                </label>
                <textarea
                  name="description"
                  class="type-form-field-input type-form-field-textarea"
                  ${isReadonly ? 'readonly' : ''}
                  ${!isReadonly ? 'required' : ''}
                  placeholder="${isReadonly ? '' : 'Describe the purpose and use case for this type...'}"
                >${this.escapeHtml(this.entity?.description || '')}</textarea>
                ${!isReadonly ? '<p class="type-form-field-help">Explain what this type is used for and when to apply it.</p>' : ''}
              </div>

              <!-- Tags Field -->
              <div class="type-form-field">
                <label class="type-form-field-label">Tags</label>
                <div class="type-form-tags ${isReadonly ? 'disabled' : ''}" data-tags-container>
                  ${this.renderTags()}
                  ${!isReadonly ? '<input type="text" class="type-form-tag-input" placeholder="Add tag..." data-tag-input />' : ''}
                </div>
                ${!isReadonly ? '<p class="type-form-field-help">Press Enter to add tags. Tags help organize and filter types.</p>' : ''}
              </div>

            </div>
          </div>

          <!-- Template/Schema Section -->
          <div class="type-form-section">
            <div class="type-form-section-header">
              <div class="type-form-section-icon">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="16 18 22 12 16 6"></polyline>
                  <polyline points="8 6 2 12 8 18"></polyline>
                </svg>
              </div>
              <h2 class="type-form-section-title">${templateLabel}</h2>
            </div>
            <div class="type-form-section-body">

              <div class="type-form-field">
                <label class="type-form-field-label">${templateLabel} Definition</label>
                <div class="type-form-schema-editor">
                  <textarea
                    name="${this.entityType === 'analysis' ? 'template' : 'schema'}"
                    class="type-form-field-input type-form-schema-textarea"
                    ${isReadonly ? 'readonly' : ''}
                    placeholder="${isReadonly ? '' : 'Enter JSON structure...'}"
                  >${this.escapeHtml(this.formatJson(this.entity?.template || this.entity?.schema || {}))}</textarea>
                </div>
                ${!isReadonly ? `<p class="type-form-field-help">JSON structure defining ${this.entityType === 'analysis' ? 'the analysis output format' : 'the data to extract from source documents'}.</p>` : ''}
              </div>

            </div>
          </div>

          <!-- Instructions Section -->
          <div class="type-form-section">
            <div class="type-form-section-header">
              <div class="type-form-section-icon">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="3"></circle>
                  <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
                </svg>
              </div>
              <h2 class="type-form-section-title">AI Instructions</h2>
            </div>
            <div class="type-form-section-body">

              <div class="type-form-field">
                <label class="type-form-field-label">Instructions</label>
                <textarea
                  name="prompt"
                  class="type-form-field-input type-form-field-textarea"
                  ${isReadonly ? 'readonly' : ''}
                  placeholder="${isReadonly ? '' : 'Provide instructions for the AI to follow when processing this type...'}"
                >${this.escapeHtml(this.entity?.prompt || this.entity?.instructions || '')}</textarea>
                ${!isReadonly ? '<p class="type-form-field-help">Guide the AI on how to perform this analysis or extraction. Be specific about requirements and expected output.</p>' : ''}
              </div>

            </div>
          </div>

        </div>
      </div>
    `;
  }

  /**
   * Render footer with action buttons
   */
  renderFooter() {
    if (this.mode === 'view') {
      return `
        <div class="type-form-footer">
          <div class="type-form-footer-left"></div>
          <div class="type-form-footer-right">
            <button class="btn btn-secondary" data-action="back">
              Back to List
            </button>
            <button class="btn btn-primary" data-action="edit">
              Edit Type
            </button>
          </div>
        </div>
      `;
    }

    return `
      <div class="type-form-footer">
        <div class="type-form-footer-left">
          <div class="type-form-dirty-indicator ${this.isDirty ? 'visible' : ''}" data-dirty-indicator>
            <span class="type-form-dirty-dot"></span>
            <span>Unsaved changes</span>
          </div>
        </div>
        <div class="type-form-footer-right">
          <button class="btn btn-secondary" data-action="cancel">
            Cancel
          </button>
          <button class="btn btn-primary" data-action="save">
            ${this.mode === 'create' ? 'Create' : 'Save Changes'}
          </button>
        </div>
      </div>
    `;
  }

  /**
   * Render tags as chips
   */
  renderTags() {
    return this.tags.map(tag => `
      <span class="type-form-tag">
        ${this.escapeHtml(tag)}
        ${this.mode !== 'view' ? `
          <button class="type-form-tag-remove" data-remove-tag="${this.escapeHtml(tag)}" type="button">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        ` : ''}
      </span>
    `).join('');
  }

  /**
   * Load entity from API
   */
  async loadEntity(id) {
    try {
      this.entity = this.entityType === 'analysis'
        ? await this.api.getAnalysisType(id)
        : await this.api.getSourceType(id);

      // Extract tags from entity
      this.tags = Array.isArray(this.entity.tags) ? [...this.entity.tags] : [];

    } catch (error) {
      console.error('Failed to load entity:', error);
      this.toast.error('Failed to load type. Please try again.');
      throw error;
    }
  }

  /**
   * Load template for new entity
   */
  async loadTemplate() {
    try {
      this.entity = this.entityType === 'analysis'
        ? await this.api.getAnalysisTypeTemplate()
        : await this.api.getSourceTypeTemplate();

      this.tags = [];

    } catch (error) {
      console.error('Failed to load template:', error);
      this.toast.error('Failed to load template. Please try again.');
      throw error;
    }
  }

  /**
   * Attach event handlers to form elements
   */
  attachEventHandlers(container) {
    if (!container) return;

    // Mark dirty on any input change
    if (this.mode !== 'view') {
      const inputs = container.querySelectorAll('input, textarea');
      inputs.forEach(input => {
        input.addEventListener('input', () => this.markDirty());
      });

      // Tags input handler
      const tagInput = container.querySelector('[data-tag-input]');
      if (tagInput) {
        tagInput.addEventListener('keydown', (e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            this.addTag(tagInput.value.trim());
            tagInput.value = '';
          }
        });
      }

      // Tag remove handlers
      const removeButtons = container.querySelectorAll('[data-remove-tag]');
      removeButtons.forEach(button => {
        button.addEventListener('click', (e) => {
          e.preventDefault();
          const tag = button.getAttribute('data-remove-tag');
          this.removeTag(tag);
        });
      });

      // Setup beforeunload warning
      this.beforeUnloadHandler = (e) => {
        if (this.isDirty) {
          e.preventDefault();
          e.returnValue = '';
          return '';
        }
      };
      window.addEventListener('beforeunload', this.beforeUnloadHandler);
    }

    // Action button handlers
    const saveBtn = container.querySelector('[data-action="save"]');
    if (saveBtn) {
      saveBtn.addEventListener('click', () => this.save());
    }

    const cancelBtn = container.querySelector('[data-action="cancel"]');
    if (cancelBtn) {
      cancelBtn.addEventListener('click', () => this.cancel());
    }

    const backBtn = container.querySelector('[data-action="back"]');
    if (backBtn) {
      backBtn.addEventListener('click', () => this.cancel());
    }

    const editBtn = container.querySelector('[data-action="edit"]');
    if (editBtn) {
      editBtn.addEventListener('click', () => this.switchToEditMode());
    }
  }

  /**
   * Clean up event handlers
   */
  cleanup() {
    if (this.beforeUnloadHandler) {
      window.removeEventListener('beforeunload', this.beforeUnloadHandler);
      this.beforeUnloadHandler = null;
    }
  }

  /**
   * Add a tag
   */
  addTag(tag) {
    if (!tag) return;

    if (this.tags.includes(tag)) {
      this.toast.info('Tag already exists');
      return;
    }

    this.tags.push(tag);
    this.updateTagsDisplay();
    this.markDirty();
  }

  /**
   * Remove a tag
   */
  removeTag(tag) {
    this.tags = this.tags.filter(t => t !== tag);
    this.updateTagsDisplay();
    this.markDirty();
  }

  /**
   * Update tags display in DOM
   */
  updateTagsDisplay() {
    const container = document.querySelector('[data-tags-container]');
    if (!container) return;

    const tagInput = container.querySelector('[data-tag-input]');
    container.innerHTML = this.renderTags();

    if (this.mode !== 'view' && tagInput) {
      container.insertAdjacentHTML('beforeend', '<input type="text" class="type-form-tag-input" placeholder="Add tag..." data-tag-input />');
      const newTagInput = container.querySelector('[data-tag-input]');
      if (newTagInput) {
        newTagInput.addEventListener('keydown', (e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            this.addTag(newTagInput.value.trim());
            newTagInput.value = '';
          }
        });
      }
    }

    // Re-attach remove button handlers
    const removeButtons = container.querySelectorAll('[data-remove-tag]');
    removeButtons.forEach(button => {
      button.addEventListener('click', (e) => {
        e.preventDefault();
        const tag = button.getAttribute('data-remove-tag');
        this.removeTag(tag);
      });
    });
  }

  /**
   * Mark form as dirty (unsaved changes)
   */
  markDirty() {
    this.isDirty = true;

    const indicator = document.querySelector('[data-dirty-indicator]');
    if (indicator) {
      indicator.classList.add('visible');
    }
  }

  /**
   * Validate form data
   */
  validateForm() {
    const errors = [];

    const nameInput = document.querySelector('input[name="name"]');
    const descriptionInput = document.querySelector('textarea[name="description"]');

    if (!nameInput?.value.trim()) {
      errors.push('Name is required');
      nameInput?.classList.add('error');
    } else {
      nameInput?.classList.remove('error');
    }

    if (!descriptionInput?.value.trim()) {
      errors.push('Description is required');
      descriptionInput?.classList.add('error');
    } else {
      descriptionInput?.classList.remove('error');
    }

    // Validate JSON in template/schema field
    const schemaField = this.entityType === 'analysis' ? 'template' : 'schema';
    const schemaInput = document.querySelector(`textarea[name="${schemaField}"]`);
    if (schemaInput?.value.trim()) {
      try {
        JSON.parse(schemaInput.value);
        schemaInput.classList.remove('error');
      } catch (e) {
        errors.push(`${schemaField} must be valid JSON`);
        schemaInput.classList.add('error');
      }
    }

    return errors;
  }

  /**
   * Get form data as object
   */
  getFormData() {
    const formData = {
      name: document.querySelector('input[name="name"]')?.value.trim() || '',
      description: document.querySelector('textarea[name="description"]')?.value.trim() || '',
      tags: [...this.tags],
      prompt: document.querySelector('textarea[name="prompt"]')?.value.trim() || ''
    };

    // Parse template/schema JSON
    const schemaField = this.entityType === 'analysis' ? 'template' : 'schema';
    const schemaInput = document.querySelector(`textarea[name="${schemaField}"]`);
    if (schemaInput?.value.trim()) {
      try {
        formData[schemaField] = JSON.parse(schemaInput.value);
      } catch (e) {
        formData[schemaField] = {};
      }
    }

    return formData;
  }

  /**
   * Save form data
   */
  async save() {
    // Validate form
    const errors = this.validateForm();
    if (errors.length > 0) {
      this.toast.error(errors[0]);
      return;
    }

    const formData = this.getFormData();

    try {
      let result;

      if (this.mode === 'create') {
        // Create new entity
        result = this.entityType === 'analysis'
          ? await this.api.createAnalysisType(formData)
          : await this.api.createSourceType(formData);

        this.toast.success(`${this.entityType === 'analysis' ? 'Analysis' : 'Source'} type created successfully`);

      } else if (this.mode === 'edit') {
        // Update existing entity
        result = this.entityType === 'analysis'
          ? await this.api.updateAnalysisType(this.entity.id, formData)
          : await this.api.updateSourceType(this.entity.id, formData);

        this.toast.success('Changes saved successfully');
      }

      // Mark as clean
      this.isDirty = false;

      // Navigate back to list
      this.navigateToList();

    } catch (error) {
      console.error('Failed to save:', error);
      this.toast.error('Failed to save. Please try again.');
    }
  }

  /**
   * Cancel and navigate back
   */
  cancel() {
    if (this.isDirty) {
      const confirmed = confirm('You have unsaved changes. Are you sure you want to leave?');
      if (!confirmed) return;
    }

    this.cleanup();
    this.navigateToList();
  }

  /**
   * Switch from view to edit mode
   */
  switchToEditMode() {
    this.cleanup();

    const route = this.entityType === 'analysis' ? 'analysis-type-edit' : 'source-type-edit';
    this.eventBus.emit('navigate', route, { id: this.entity.id });
  }

  /**
   * Navigate back to list
   */
  navigateToList() {
    const route = this.entityType === 'analysis' ? 'analysis-types-list' : 'source-types-list';
    this.eventBus.emit('navigate', route);
  }

  /**
   * Format JSON for display
   */
  formatJson(obj) {
    if (!obj || typeof obj !== 'object') return '';
    return JSON.stringify(obj, null, 2);
  }

  /**
   * Escape HTML to prevent XSS
   */
  escapeHtml(text) {
    if (text == null) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
