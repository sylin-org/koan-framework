/**
 * AICreateTypeModal - Professional AI Create interface for types
 * Three-step flow: Input → Loading → Preview
 * Separate instances for Analysis Types and Source Types
 */
import { Modal } from './Modal.js';

export class AICreateTypeModal extends Modal {
  constructor(typeCategory, api, toast) {
    const title = typeCategory === 'analysis'
      ? 'AI Create Analysis Type'
      : 'AI Create Source Type';

    super({
      title,
      size: 'large',
      closeOnOverlay: false // Don't close on overlay click during AI generation
    });

    this.typeCategory = typeCategory; // 'analysis' or 'source'
    this.api = api;
    this.toast = toast;
    this.currentStep = 1; // 1 = Input, 2 = Loading, 3 = Preview
    this.aiSuggestion = null;
  }

  /**
   * Open the AI Create modal
   * @returns {Promise} Resolves with created type or null if cancelled
   */
  async openAICreate() {
    this.currentStep = 1;
    this.aiSuggestion = null;

    // Render step 1 content
    const content = this.renderInputForm();
    const buttons = [
      { label: 'Cancel', action: 'cancel' },
      { label: 'Generate with AI', action: 'generate', primary: true }
    ];

    // Open modal and wait for interaction
    const modalPromise = this.open(content, buttons);

    // Set up custom event handler for modal actions
    this.dialog.addEventListener('modal-action', async (e) => {
      await this.handleModalAction(e.detail.action);
    });

    return modalPromise;
  }

  /**
   * Handle modal actions
   */
  async handleModalAction(action) {
    switch (action) {
      case 'cancel':
        this.close(null);
        break;

      case 'generate':
        await this.generateType();
        break;

      case 'regenerate':
        await this.generateType();
        break;

      case 'create':
        await this.createType();
        break;

      case 'edit-field':
        // Allow editing in preview
        break;
    }
  }

  /**
   * Render Step 1: Input Form
   */
  renderInputForm() {
    const examples = this.getExamples();

    return `
      <div class="ai-create-form">
        <div class="form-section">
          <label class="form-label">
            What do you want to ${this.typeCategory === 'analysis' ? 'analyze' : 'process'}?
            <span class="required">*</span>
          </label>
          <textarea
            name="goal"
            class="form-input"
            rows="3"
            placeholder="${examples[0].goal}"
            required
            autofocus
          ></textarea>
          <p class="form-help">
            ${this.typeCategory === 'analysis'
              ? 'Describe the analysis goal in 1-2 sentences. Be specific about what insights you need.'
              : 'Describe what kind of source documents you want to process and what information to extract.'
            }
          </p>
        </div>

        <div class="form-section">
          <label class="form-label">
            Who is the audience?
            <span class="required">*</span>
          </label>
          <input
            type="text"
            name="audience"
            class="form-input"
            placeholder="${examples[0].audience}"
            required
          />
          <p class="form-help">
            Who will use ${this.typeCategory === 'analysis' ? 'these insights' : 'this data'}? This helps tailor the output format.
          </p>
        </div>

        <div class="form-section form-section-collapsible">
          <button class="form-section-toggle" type="button" aria-expanded="false" data-action="toggle-context">
            <svg class="icon-chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="6 9 12 15 18 9"></polyline>
            </svg>
            Additional Context (optional)
          </button>
          <div class="form-section-content" hidden>
            <textarea
              name="additionalContext"
              class="form-input"
              rows="4"
              placeholder="Any specific requirements, data formats, or constraints..."
            ></textarea>
            <p class="form-help">
              Provide any additional context that will help the AI generate a more accurate type definition.
            </p>
          </div>
        </div>

        <div class="form-examples">
          <details>
            <summary>Show Examples</summary>
            <div class="example-list">
              ${examples.map(ex => `
                <div class="example-item">
                  <strong>Goal:</strong> ${this.escapeHtml(ex.goal)}
                  <br>
                  <strong>Audience:</strong> ${this.escapeHtml(ex.audience)}
                </div>
              `).join('')}
            </div>
          </details>
        </div>
      </div>
    `;
  }

  /**
   * Get examples based on type category
   */
  getExamples() {
    if (this.typeCategory === 'analysis') {
      return [
        {
          goal: 'Extract key financial metrics from quarterly earnings reports',
          audience: 'Executive team and investors'
        },
        {
          goal: 'Analyze contract terms to identify non-standard clauses and risks',
          audience: 'Legal team and contract managers'
        },
        {
          goal: 'Identify customer pain points and feature requests from support tickets',
          audience: 'Product managers and customer success team'
        }
      ];
    } else {
      return [
        {
          goal: 'Process customer support tickets to extract issue category, priority, and sentiment',
          audience: 'Customer support team'
        },
        {
          goal: 'Extract structured data from invoices (vendor, amount, date, line items)',
          audience: 'Accounting and finance team'
        },
        {
          goal: 'Parse resumes to extract skills, experience, and education details',
          audience: 'HR and recruiting team'
        }
      ];
    }
  }

  /**
   * Step 2: Generate type with AI
   */
  async generateType() {
    // Get form data
    const formData = this.getFormData();

    // Validate required fields
    if (!formData.goal || !formData.audience) {
      this.toast.error('Please fill in all required fields');
      return;
    }

    // Show loading state
    this.currentStep = 2;
    this.setLoading(true, 'AI is generating type definition...');

    try {
      // Call appropriate API method
      const suggestion = this.typeCategory === 'analysis'
        ? await this.api.suggestAnalysisType(
            formData.goal,
            formData.audience,
            formData.additionalContext || ''
          )
        : await this.api.suggestSourceType(
            formData.goal,
            formData.audience,
            formData.additionalContext || ''
          );

      this.aiSuggestion = suggestion;

      // Move to preview step
      this.currentStep = 3;
      this.showPreview();

    } catch (error) {
      console.error('Failed to generate type:', error);
      this.toast.error('Failed to generate type. Please try again.');

      // Return to input form
      this.currentStep = 1;
      const content = this.renderInputForm();
      this.updateContent(content);
      this.setLoading(false);

      // Restore form data
      setTimeout(() => {
        this.setFormData(formData);
        this.attachInputFormHandlers();
      }, 100);
    }
  }

  /**
   * Step 3: Show preview of AI suggestion
   */
  showPreview() {
    const content = this.renderPreview();
    this.updateContent(content);

    const buttons = [
      { label: 'Regenerate', action: 'regenerate' },
      { label: 'Create Type', action: 'create', primary: true }
    ];
    this.updateFooter(buttons);

    // Attach preview handlers
    this.attachPreviewHandlers();
  }

  /**
   * Render Step 3: Preview
   */
  renderPreview() {
    if (!this.aiSuggestion) return '';

    const suggestion = this.aiSuggestion;

    return `
      <div class="ai-preview">
        <div class="preview-header">
          <svg class="icon-ai" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="3"></circle>
            <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
          </svg>
          <h3>AI Generated Suggestion</h3>
        </div>

        <div class="form-section">
          <label class="form-label">Type Name</label>
          <input
            type="text"
            name="name"
            class="form-input"
            value="${this.escapeHtml(suggestion.name || '')}"
          />
          <p class="form-help">You can edit this name if needed.</p>
        </div>

        <div class="form-section">
          <label class="form-label">Description</label>
          <textarea
            name="description"
            class="form-input"
            rows="3"
          >${this.escapeHtml(suggestion.description || '')}</textarea>
          <p class="form-help">You can edit this description if needed.</p>
        </div>

        ${suggestion.template || suggestion.schema ? `
          <div class="form-section">
            <label class="form-label">Generated ${this.typeCategory === 'analysis' ? 'Template' : 'Schema'}</label>
            <div class="schema-preview">
              <pre><code>${this.escapeHtml(JSON.stringify(suggestion.template || suggestion.schema, null, 2))}</code></pre>
            </div>
            <p class="form-help">
              This ${this.typeCategory === 'analysis' ? 'template' : 'schema'} defines the structure of extracted data. You can further customize it after creation.
            </p>
          </div>
        ` : ''}

        ${suggestion.prompt || suggestion.instructions ? `
          <div class="form-section">
            <label class="form-label">AI Instructions</label>
            <textarea
              name="prompt"
              class="form-input"
              rows="4"
              readonly
            >${this.escapeHtml(suggestion.prompt || suggestion.instructions || '')}</textarea>
            <p class="form-help">These instructions guide the AI during extraction.</p>
          </div>
        ` : ''}
      </div>
    `;
  }

  /**
   * Create the type from AI suggestion
   */
  async createType() {
    // Get edited form data from preview
    const formData = this.getFormData();

    // Merge with AI suggestion
    const typeData = {
      ...this.aiSuggestion,
      name: formData.name || this.aiSuggestion.name,
      description: formData.description || this.aiSuggestion.description,
    };

    try {
      // Call appropriate API method
      const createdType = this.typeCategory === 'analysis'
        ? await this.api.createAnalysisType(typeData)
        : await this.api.createSourceType(typeData);

      this.toast.success(`${this.typeCategory === 'analysis' ? 'Analysis' : 'Source'} type created successfully`);

      // Close modal and return created type
      this.close(createdType);

    } catch (error) {
      console.error('Failed to create type:', error);
      this.toast.error('Failed to create type. Please try again.');
    }
  }

  /**
   * Attach event handlers for input form
   */
  attachInputFormHandlers() {
    if (!this.dialog) return;

    // Toggle collapsible section
    const toggle = this.dialog.querySelector('[data-action="toggle-context"]');
    if (toggle) {
      toggle.addEventListener('click', () => {
        const isExpanded = toggle.getAttribute('aria-expanded') === 'true';
        toggle.setAttribute('aria-expanded', !isExpanded);

        const content = toggle.nextElementSibling;
        if (content) {
          content.hidden = isExpanded;
        }
      });
    }
  }

  /**
   * Attach event handlers for preview
   */
  attachPreviewHandlers() {
    // Currently no special handlers needed for preview
    // Form fields are editable by default
  }

  /**
   * Override open method to attach input form handlers
   */
  async open(content, footerButtons) {
    const result = await super.open(content, footerButtons);

    // Attach input form handlers after modal is rendered
    setTimeout(() => {
      this.attachInputFormHandlers();
    }, 100);

    return result;
  }
}
