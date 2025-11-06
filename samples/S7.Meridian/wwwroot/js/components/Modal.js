/**
 * Modal - Reusable modal component
 * Foundation for AI Create and other modal dialogs
 */
export class Modal {
  constructor(options = {}) {
    this.title = options.title || '';
    this.size = options.size || 'medium'; // 'small', 'medium', 'large'
    this.closeOnOverlay = options.closeOnOverlay !== false;
    this.closeOnEscape = options.closeOnEscape !== false;

    this.overlay = null;
    this.dialog = null;
    this.resolvePromise = null;
    this.isOpen = false;
  }

  /**
   * Open modal with content
   * @param {string} content - HTML content for modal body
   * @param {Array} footerButtons - Array of button configs
   * @returns {Promise} Resolves with result when modal closes
   */
  open(content, footerButtons = []) {
    if (this.isOpen) {
      console.warn('Modal is already open');
      return Promise.resolve(null);
    }

    return new Promise((resolve) => {
      this.resolvePromise = resolve;
      this.render(content, footerButtons);
      this.attachEventHandlers();
      this.isOpen = true;

      // Focus first input
      setTimeout(() => {
        const firstInput = this.dialog.querySelector('input, textarea, button');
        if (firstInput) {
          firstInput.focus();
        }
      }, 100);
    });
  }

  /**
   * Render modal HTML
   */
  render(content, footerButtons) {
    // Create overlay
    this.overlay = document.createElement('div');
    this.overlay.className = 'modal-overlay';

    // Create dialog
    this.dialog = document.createElement('div');
    this.dialog.className = `modal-dialog modal-${this.size}`;

    // Header
    const header = document.createElement('div');
    header.className = 'modal-header';
    header.innerHTML = `
      <h2 class="modal-title">${this.escapeHtml(this.title)}</h2>
      <button class="modal-close" aria-label="Close" data-action="close">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="18" y1="6" x2="6" y2="18"></line>
          <line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
      </button>
    `;

    // Body
    const body = document.createElement('div');
    body.className = 'modal-body';
    body.innerHTML = content;

    // Footer
    const footer = document.createElement('div');
    footer.className = 'modal-footer';

    if (footerButtons.length > 0) {
      footer.innerHTML = footerButtons.map(btn => {
        const btnClass = btn.primary ? 'btn btn-primary' : 'btn btn-secondary';
        const disabled = btn.disabled ? 'disabled' : '';
        return `
          <button
            class="${btnClass}"
            data-action="${btn.action}"
            ${disabled}
          >
            ${btn.icon ? `<svg class="icon" width="16" height="16">${btn.icon}</svg>` : ''}
            ${this.escapeHtml(btn.label)}
          </button>
        `;
      }).join('');
    }

    // Assemble
    this.dialog.appendChild(header);
    this.dialog.appendChild(body);
    if (footerButtons.length > 0) {
      this.dialog.appendChild(footer);
    }

    this.overlay.appendChild(this.dialog);
    document.body.appendChild(this.overlay);

    // Trigger animation
    requestAnimationFrame(() => {
      this.overlay.classList.add('modal-open');
    });
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers() {
    // Close on overlay click
    if (this.closeOnOverlay) {
      this.overlay.addEventListener('click', (e) => {
        if (e.target === this.overlay) {
          this.close(null);
        }
      });
    }

    // Close on escape key
    if (this.closeOnEscape) {
      this.handleEscape = (e) => {
        if (e.key === 'Escape') {
          this.close(null);
        }
      };
      document.addEventListener('keydown', this.handleEscape);
    }

    // Close button
    const closeBtn = this.dialog.querySelector('[data-action="close"]');
    if (closeBtn) {
      closeBtn.addEventListener('click', () => this.close(null));
    }

    // Footer buttons - emit events instead of closing directly
    const footerButtons = this.dialog.querySelectorAll('.modal-footer button[data-action]');
    footerButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const action = btn.dataset.action;
        if (action !== 'close') {
          this.handleAction(action);
        }
      });
    });
  }

  /**
   * Handle button action (to be overridden or handled externally)
   */
  handleAction(action) {
    // This can be overridden by subclasses or handled via custom event listeners
    const event = new CustomEvent('modal-action', { detail: { action } });
    this.dialog.dispatchEvent(event);
  }

  /**
   * Close modal
   * @param {*} result - Result to resolve promise with
   */
  close(result = null) {
    if (!this.isOpen) return;

    // Remove escape handler
    if (this.handleEscape) {
      document.removeEventListener('keydown', this.handleEscape);
    }

    // Fade out
    this.overlay.classList.remove('modal-open');

    // Remove from DOM after animation
    setTimeout(() => {
      if (this.overlay && this.overlay.parentNode) {
        this.overlay.parentNode.removeChild(this.overlay);
      }
      this.overlay = null;
      this.dialog = null;
      this.isOpen = false;

      // Resolve promise
      if (this.resolvePromise) {
        this.resolvePromise(result);
        this.resolvePromise = null;
      }
    }, 300);
  }

  /**
   * Update modal content
   * @param {string} content - New HTML content
   */
  updateContent(content) {
    if (!this.dialog) return;

    const body = this.dialog.querySelector('.modal-body');
    if (body) {
      body.innerHTML = content;
    }
  }

  /**
   * Update footer buttons
   * @param {Array} buttons - New button configs
   */
  updateFooter(buttons) {
    if (!this.dialog) return;

    const footer = this.dialog.querySelector('.modal-footer');
    if (!footer) return;

    footer.innerHTML = buttons.map(btn => {
      const btnClass = btn.primary ? 'btn btn-primary' : 'btn btn-secondary';
      const disabled = btn.disabled ? 'disabled' : '';
      return `
        <button
          class="${btnClass}"
          data-action="${btn.action}"
          ${disabled}
        >
          ${btn.icon ? `<svg class="icon" width="16" height="16">${btn.icon}</svg>` : ''}
          ${this.escapeHtml(btn.label)}
        </button>
      `;
    }).join('');

    // Re-attach event handlers for new buttons
    const footerButtons = footer.querySelectorAll('button[data-action]');
    footerButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const action = btn.dataset.action;
        if (action === 'close') {
          this.close(null);
        } else {
          this.handleAction(action);
        }
      });
    });
  }

  /**
   * Set loading state
   * @param {boolean} isLoading - Loading state
   * @param {string} message - Loading message
   */
  setLoading(isLoading, message = 'Loading...') {
    if (!this.dialog) return;

    if (isLoading) {
      const loadingHtml = `
        <div class="modal-loading">
          <svg class="spinner" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10"></circle>
          </svg>
          <p>${this.escapeHtml(message)}</p>
        </div>
      `;
      this.updateContent(loadingHtml);

      // Disable footer buttons
      const footerButtons = this.dialog.querySelectorAll('.modal-footer button');
      footerButtons.forEach(btn => btn.disabled = true);
    } else {
      // Re-enable footer buttons
      const footerButtons = this.dialog.querySelectorAll('.modal-footer button');
      footerButtons.forEach(btn => btn.disabled = false);
    }
  }

  /**
   * Get form data from modal
   * @returns {Object} Form data as key-value pairs
   */
  getFormData() {
    if (!this.dialog) return {};

    const formData = {};
    const inputs = this.dialog.querySelectorAll('input, textarea, select');

    inputs.forEach(input => {
      if (input.name) {
        if (input.type === 'checkbox') {
          formData[input.name] = input.checked;
        } else if (input.type === 'radio') {
          if (input.checked) {
            formData[input.name] = input.value;
          }
        } else {
          formData[input.name] = input.value;
        }
      }
    });

    return formData;
  }

  /**
   * Set form data
   * @param {Object} data - Data to populate form with
   */
  setFormData(data) {
    if (!this.dialog) return;

    Object.entries(data).forEach(([key, value]) => {
      const input = this.dialog.querySelector(`[name="${key}"]`);
      if (input) {
        if (input.type === 'checkbox') {
          input.checked = value;
        } else if (input.type === 'radio') {
          const radio = this.dialog.querySelector(`[name="${key}"][value="${value}"]`);
          if (radio) radio.checked = true;
        } else {
          input.value = value;
        }
      }
    });
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
