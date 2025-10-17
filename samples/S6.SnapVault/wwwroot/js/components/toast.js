/**
 * Toast Notification Component
 * Temporary notifications with actions
 */

export class Toast {
  constructor() {
    this.container = document.querySelector('.toast-container');
    this.toasts = new Map();
  }

  show(message, options = {}) {
    const {
      icon = '',
      duration = 3000,
      actions = [],
      allowHtml = false
    } = options;

    const toast = document.createElement('div');
    toast.className = 'toast';
    const toastId = Date.now() + Math.random();

    const content = allowHtml ? message : this.escapeHtml(message);

    toast.innerHTML = `
      <div class="toast-content">
        ${icon ? `<span class="toast-icon">${icon}</span>` : ''}
        <div class="toast-message">${content}</div>
      </div>
      ${actions.length > 0 ? `
        <div class="toast-actions">
          ${actions.map((action, index) => `
            <button class="toast-action" data-action-index="${index}">
              ${this.escapeHtml(action.label)}
            </button>
          `).join('')}
        </div>
      ` : ''}
      <button class="toast-close" aria-label="Close">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="18" y1="6" x2="6" y2="18"></line>
          <line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
      </button>
    `;

    // Close button
    toast.querySelector('.toast-close').addEventListener('click', () => {
      this.hide(toastId);
    });

    // Action buttons
    if (actions.length > 0) {
      toast.querySelectorAll('.toast-action').forEach((btn, index) => {
        btn.addEventListener('click', () => {
          if (actions[index].onClick) {
            actions[index].onClick();
          }
          this.hide(toastId);
        });
      });
    }

    this.container.appendChild(toast);
    this.toasts.set(toastId, toast);

    // Animate in
    requestAnimationFrame(() => {
      toast.classList.add('show');
    });

    // Auto-hide
    if (duration > 0) {
      setTimeout(() => {
        this.hide(toastId);
      }, duration);
    }

    return toastId;
  }

  hide(toastId) {
    const toast = this.toasts.get(toastId);
    if (!toast) return;

    toast.classList.remove('show');
    toast.classList.add('hide');

    setTimeout(() => {
      toast.remove();
      this.toasts.delete(toastId);
    }, 200);
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}
