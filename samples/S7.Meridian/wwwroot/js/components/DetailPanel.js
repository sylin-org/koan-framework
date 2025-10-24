/**
 * DetailPanel Component
 * Shared slide-in panel for viewing and editing entity details
 */

export class DetailPanel {
  constructor(eventBus) {
    this.eventBus = eventBus;
    this.isOpen = false;
    this.mode = 'view';
    this.data = null;
    this.onSave = null;
    this.onDelete = null;
    this.previousFocus = null;

    this.boundHandleKeydown = this.handleKeydown.bind(this);
  }

  open(data, mode = 'view') {
    this.data = data;
    this.mode = mode;
    this.isOpen = true;
    this.previousFocus = document.activeElement;

    this.render();

    requestAnimationFrame(() => {
      const panel = document.querySelector('.detail-panel');
      const backdrop = document.querySelector('.detail-panel-backdrop');
      backdrop?.classList.add('visible');
      panel?.classList.add('open');
      panel?.querySelector('.detail-panel-close')?.focus();
      this.trapFocus(panel);
    });

    document.addEventListener('keydown', this.boundHandleKeydown);
    this.eventBus.emit('detail-panel:opened', { data, mode });
  }

  close() {
    const panel = document.querySelector('.detail-panel');
    const backdrop = document.querySelector('.detail-panel-backdrop');

    backdrop?.classList.remove('visible');
    panel?.classList.remove('open');

    setTimeout(() => {
      panel?.remove();
      backdrop?.remove();
      this.isOpen = false;
      document.removeEventListener('keydown', this.boundHandleKeydown);

      if (this.previousFocus && typeof this.previousFocus.focus === 'function') {
        this.previousFocus.focus();
      }

      this.eventBus.emit('detail-panel:closed');
    }, 320);
  }

  switchMode(newMode) {
    this.mode = newMode;
    const panel = document.querySelector('.detail-panel');
    if (!panel) return;

    panel.classList.toggle('edit-mode', newMode === 'edit');
    const body = panel.querySelector('.detail-panel-body');
    const footer = panel.querySelector('.detail-panel-footer');

    if (body) {
      body.innerHTML = this.renderBody();
      this.attachBodyEventHandlers();
    }

    if (footer) {
      footer.innerHTML = this.renderFooter();
      this.attachFooterEventHandlers();
    }
  }

  render() {
    document.querySelector('.detail-panel')?.remove();
    document.querySelector('.detail-panel-backdrop')?.remove();

    const markup = `
      <div class="detail-panel-backdrop" aria-hidden="true"></div>
      <aside class="detail-panel ${this.mode === 'edit' ? 'edit-mode' : ''}" role="dialog" aria-modal="true" aria-labelledby="panel-title">
        <header class="detail-panel-header">
          <h2 id="panel-title" class="detail-panel-title">${this.escapeHtml(this.computeTitle())}</h2>
          <button class="detail-panel-close" type="button" aria-label="Close detail panel">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </header>
        <div class="detail-panel-body">
          ${this.renderBody()}
        </div>
        <footer class="detail-panel-footer">
          ${this.renderFooter()}
        </footer>
      </aside>
    `;

    document.body.insertAdjacentHTML('beforeend', markup);
    this.attachEventHandlers();
  }

  computeTitle() {
    if (!this.data) return 'Details';
    if (this.mode === 'edit') {
      return this.data.name ? `Edit · ${this.data.name}` : 'Edit Details';
    }
    return this.data.name || 'Details';
  }

  renderBody() {
    if (!this.data) {
      return `
        <div class="panel-empty-state">
          <div class="detail-panel-spinner" aria-hidden="true"></div>
          <p>Loading details…</p>
        </div>
      `;
    }

    return `
      ${this.renderMetadata()}
      <div class="panel-section">
        <h3 class="panel-section-header">Description</h3>
        <div class="panel-section-content">
          ${this.mode === 'view'
            ? `<p>${this.escapeHtml(this.data.description || 'No description yet.')}</p>`
            : `<div class="panel-field-input"><textarea name="description" rows="4">${this.escapeHtml(this.data.description || '')}</textarea></div>`}
        </div>
      </div>
      ${this.renderTagSection()}
    `;
  }

  renderMetadata() {
    const entries = this.data?.meta ? Object.entries(this.data.meta) : [];
    if (!entries.length) {
      return '';
    }

    const content = entries.map(([label, value]) => `
      <div class="panel-field">
        <span class="panel-field-label">${this.escapeHtml(label)}</span>
        <span class="panel-field-value">${this.escapeHtml(value)}</span>
      </div>
    `).join('');

    return `
      <div class="panel-section">
        <h3 class="panel-section-header">Summary</h3>
        <div class="panel-section-content panel-metadata">
          ${content}
        </div>
      </div>
    `;
  }

  renderTagSection() {
    if (!this.data?.tags || this.data.tags.length === 0) {
      return '';
    }

    return `
      <div class="panel-section">
        <h3 class="panel-section-header">Tags</h3>
        <div class="panel-tags">
          ${this.data.tags.map((tag) => `<span class="panel-tag">${this.escapeHtml(tag)}</span>`).join('')}
        </div>
      </div>
    `;
  }

  renderFooter() {
    if (this.mode === 'edit') {
      return `
        <button class="btn btn-danger" type="button" data-action="delete">Delete</button>
        <div class="footer-actions-right">
          <button class="btn btn-secondary" type="button" data-action="cancel">Cancel</button>
          <button class="btn btn-primary" type="button" data-action="save">Save Changes</button>
        </div>
      `;
    }

    return `
      <button class="btn btn-danger" type="button" data-action="delete">Delete</button>
      <div class="footer-actions-right">
        ${this.data?.links?.workspace ? '<button class="btn btn-secondary" type="button" data-action="open-workspace">Open Workspace</button>' : ''}
        <button class="btn btn-secondary" type="button" data-action="close">Close</button>
        <button class="btn btn-primary" type="button" data-action="edit">Edit</button>
      </div>
    `;
  }

  attachEventHandlers() {
    document.querySelector('.detail-panel-close')?.addEventListener('click', () => this.close());
    document.querySelector('.detail-panel-backdrop')?.addEventListener('click', () => this.close());
    this.attachFooterEventHandlers();
    this.attachBodyEventHandlers();
  }

  attachFooterEventHandlers() {
    const footer = document.querySelector('.detail-panel-footer');
    if (!footer) return;

    footer.addEventListener('click', (event) => {
      const action = event.target.closest('[data-action]')?.dataset.action;
      if (!action) return;

      switch (action) {
        case 'close':
          this.close();
          break;
        case 'open-workspace':
          this.close();
          this.eventBus.emit('detail-panel:open-workspace', this.data?.id);
          break;
        case 'edit':
          this.switchMode('edit');
          break;
        case 'cancel':
          this.switchMode('view');
          break;
        case 'save':
          this.handleSave();
          break;
        case 'delete':
          this.handleDelete();
          break;
        default:
          break;
      }
    });
  }

  attachBodyEventHandlers() {
    // Placeholder for overrides (e.g., selects, chip editors)
  }

  async handleSave() {
    if (typeof this.onSave !== 'function') {
      return;
    }

    const saveButton = document.querySelector('[data-action="save"]');
    if (saveButton) {
      saveButton.disabled = true;
      saveButton.textContent = 'Saving…';
    }

    try {
      const formData = this.collectFormData();
      await this.onSave(formData);
      this.data = { ...this.data, ...formData };
      this.switchMode('view');
      this.eventBus.emit('detail-panel:saved', formData);
    } catch (error) {
      console.error('Detail panel save failed', error);
      this.eventBus.emit('detail-panel:save-error', error);
    } finally {
      if (saveButton) {
        saveButton.disabled = false;
        saveButton.textContent = 'Save Changes';
      }
    }
  }

  async handleDelete() {
    if (typeof this.onDelete !== 'function' || !this.data) {
      return;
    }

    const confirmed = window.confirm(`Delete "${this.data.name}"? This cannot be undone.`);
    if (!confirmed) return;

    try {
      await this.onDelete(this.data.id);
      this.close();
      this.eventBus.emit('detail-panel:deleted', this.data.id);
    } catch (error) {
      console.error('Detail panel delete failed', error);
      this.eventBus.emit('detail-panel:delete-error', error);
    }
  }

  collectFormData() {
    const panel = document.querySelector('.detail-panel-body');
    if (!panel) return {};

    const data = {};
    panel.querySelectorAll('input[name], textarea[name], select[name]').forEach((input) => {
      data[input.name] = input.value;
    });

    return data;
  }

  handleKeydown(event) {
    if (!this.isOpen) return;

    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
    } else if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 's' && this.mode === 'edit') {
      event.preventDefault();
      this.handleSave();
    } else if (event.key.toLowerCase() === 'e' && this.mode === 'view') {
      const targetTag = event.target.tagName.toLowerCase();
      if (targetTag !== 'input' && targetTag !== 'textarea') {
        event.preventDefault();
        this.switchMode('edit');
      }
    }
  }

  trapFocus(panel) {
    if (!panel) return;

    const focusable = panel.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    panel.addEventListener('keydown', (event) => {
      if (event.key !== 'Tab') return;

      if (event.shiftKey) {
        if (document.activeElement === first) {
          event.preventDefault();
          last.focus();
        }
      } else if (document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    });
  }

  escapeHtml(text) {
    if (text === null || text === undefined) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
