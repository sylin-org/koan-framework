/**
 * Alert Component
 * Displays notification messages with different variants
 */
export class Alert {
  constructor(options) {
    this.message = options.message;
    this.variant = options.variant || 'info'; // 'info', 'success', 'warning', 'danger'
    this.title = options.title;
    this.dismissible = options.dismissible !== undefined ? options.dismissible : true;
    this.onDismiss = options.onDismiss;
  }

  render() {
    const alertClass = `alert-${this.variant}`;

    return `
      <div class="alert ${alertClass}" role="alert">
        <div class="alert-content">
          ${this.title ? `<strong class="alert-title">${this.title}</strong>` : ''}
          <div class="alert-message">${this.message}</div>
        </div>
        ${this.dismissible ? `
          <button class="alert-dismiss" aria-label="Dismiss">
            <span aria-hidden="true">&times;</span>
          </button>
        ` : ''}
      </div>
    `;
  }

  static show(container, options) {
    const alert = new Alert(options);
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      const alertEl = document.createElement('div');
      alertEl.innerHTML = alert.render();
      const alertNode = alertEl.firstElementChild;

      if (alert.dismissible) {
        const dismissBtn = alertNode.querySelector('.alert-dismiss');
        dismissBtn.addEventListener('click', () => {
          alertNode.style.opacity = '0';
          alertNode.style.transform = 'translateY(-10px)';
          setTimeout(() => {
            alertNode.remove();
            if (alert.onDismiss) {
              alert.onDismiss();
            }
          }, 200);
        });
      }

      container.appendChild(alertNode);

      // Auto-dismiss after 5 seconds for info/success
      if ((options.variant === 'info' || options.variant === 'success') && alert.dismissible) {
        setTimeout(() => {
          if (alertNode.parentNode) {
            alertNode.querySelector('.alert-dismiss')?.click();
          }
        }, 5000);
      }
    }
  }

  static clear(container) {
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      const alerts = container.querySelectorAll('.alert');
      alerts.forEach(alert => alert.remove());
    }
  }
}

// Add styles for Alert
const style = document.createElement('style');
style.textContent = `
  .alert {
    position: relative;
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    transition: opacity var(--transition-fast), transform var(--transition-fast);
  }

  .alert-content {
    flex: 1;
  }

  .alert-title {
    display: block;
    margin-bottom: var(--spacing-1);
    font-weight: var(--font-weight-semibold);
  }

  .alert-message {
    font-size: var(--font-size-sm);
  }

  .alert-dismiss {
    background: none;
    border: none;
    font-size: var(--font-size-xl);
    line-height: 1;
    color: inherit;
    opacity: 0.7;
    cursor: pointer;
    padding: 0;
    margin-left: var(--spacing-3);
    transition: opacity var(--transition-fast);
  }

  .alert-dismiss:hover {
    opacity: 1;
  }
`;
document.head.appendChild(style);
