/**
 * Toast - Notification component
 * Borrowed from SnapVault for user feedback
 */
export class Toast {
  constructor() {
    this.container = document.querySelector('.toast-container');
    if (!this.container) {
      this.container = document.createElement('div');
      this.container.className = 'toast-container';
      document.body.appendChild(this.container);
    }
  }

  /**
   * Show a toast message
   * @param {string} message - Message to display
   * @param {Object} options - Toast options
   * @param {string} options.icon - Icon emoji
   * @param {number} options.duration - Duration in ms (0 = persistent)
   * @param {string} options.type - Type: info, success, warning, error
   */
  show(message, options = {}) {
    const {
      icon = null,
      duration = 3000,
      type = 'info',
    } = options;

    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;

    let html = '';

    if (icon) {
      html += `<span class="toast-icon">${icon}</span>`;
    }

    html += `<span class="toast-message">${message}</span>`;
    html += `
      <button class="toast-close" aria-label="Close">
        <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="18" y1="6" x2="6" y2="18"></line>
          <line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
      </button>
    `;

    toast.innerHTML = html;

    // Close button handler
    const closeBtn = toast.querySelector('.toast-close');
    closeBtn.addEventListener('click', () => {
      this.remove(toast);
    });

    // Add to container
    this.container.appendChild(toast);

    // Auto-remove after duration
    if (duration > 0) {
      setTimeout(() => {
        this.remove(toast);
      }, duration);
    }

    return toast;
  }

  /**
   * Remove a toast
   * @param {HTMLElement} toast - Toast element to remove
   */
  remove(toast) {
    toast.style.opacity = '0';
    toast.style.transform = 'translateX(100%)';

    setTimeout(() => {
      if (toast.parentNode) {
        toast.parentNode.removeChild(toast);
      }
    }, 300);
  }

  /**
   * Show success toast
   */
  success(message, options = {}) {
    return this.show(message, { ...options, icon: '✓', type: 'success' });
  }

  /**
   * Show error toast
   */
  error(message, options = {}) {
    return this.show(message, { ...options, icon: '⚠️', type: 'error' });
  }

  /**
   * Show warning toast
   */
  warning(message, options = {}) {
    return this.show(message, { ...options, icon: '⚠', type: 'warning' });
  }

  /**
   * Show info toast
   */
  info(message, options = {}) {
    return this.show(message, { ...options, icon: 'ℹ️', type: 'info' });
  }
}
