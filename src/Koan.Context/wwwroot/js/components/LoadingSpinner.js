/**
 * LoadingSpinner Component
 * Displays a loading indicator with optional message
 */
export class LoadingSpinner {
  constructor(options = {}) {
    this.message = options.message || 'Loading...';
    this.size = options.size || 'md'; // 'sm', 'md', 'lg'
    this.variant = options.variant || 'primary'; // 'primary', 'secondary'
    this.fullPage = options.fullPage || false;
  }

  render() {
    const sizeClass = `spinner-${this.size}`;
    const variantClass = `spinner-${this.variant}`;

    const spinnerHtml = `
      <div class="loading-spinner ${this.fullPage ? 'loading-spinner-fullpage' : ''}">
        <div class="spinner-container">
          <div class="spinner ${sizeClass} ${variantClass}"></div>
          ${this.message ? `<p class="spinner-message">${this.message}</p>` : ''}
        </div>
      </div>
    `;

    return spinnerHtml;
  }

  static show(container, options = {}) {
    const spinner = new LoadingSpinner(options);
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      container.innerHTML = spinner.render();
    }
  }

  static hide(container) {
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      const spinnerEl = container.querySelector('.loading-spinner');
      if (spinnerEl) {
        spinnerEl.remove();
      }
    }
  }
}

// Add styles for LoadingSpinner
const style = document.createElement('style');
style.textContent = `
  .loading-spinner {
    display: flex;
    justify-content: center;
    align-items: center;
    padding: var(--spacing-8);
  }

  .loading-spinner-fullpage {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background-color: var(--color-background-overlay);
    z-index: var(--z-index-modal);
  }

  .spinner-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: var(--spacing-3);
  }

  .spinner {
    border-radius: 50%;
    animation: spin 0.75s linear infinite;
  }

  .spinner-sm {
    width: 1rem;
    height: 1rem;
    border: 2px solid currentColor;
    border-right-color: transparent;
  }

  .spinner-md {
    width: 2rem;
    height: 2rem;
    border: 3px solid currentColor;
    border-right-color: transparent;
  }

  .spinner-lg {
    width: 3rem;
    height: 3rem;
    border: 4px solid currentColor;
    border-right-color: transparent;
  }

  .spinner-primary {
    color: var(--color-primary-600);
  }

  .spinner-secondary {
    color: var(--color-gray-600);
  }

  .spinner-message {
    font-size: var(--font-size-sm);
    color: var(--color-text-secondary);
    text-align: center;
  }

  .loading-spinner-fullpage .spinner-message {
    color: var(--color-text-inverse);
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
`;
document.head.appendChild(style);
