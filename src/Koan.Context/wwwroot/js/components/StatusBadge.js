/**
 * StatusBadge Component
 * Reusable badge component for status indicators
 */
export class StatusBadge {
  constructor(options) {
    this.text = options.text;
    this.variant = options.variant || 'default'; // 'default', 'primary', 'success', 'warning', 'danger', 'gray'
    this.size = options.size || 'md'; // 'sm', 'md', 'lg'
    this.dot = options.dot || false; // Show colored dot indicator
  }

  render() {
    const variantClass = `badge-${this.variant}`;
    const sizeClass = this.size !== 'md' ? `badge-${this.size}` : '';

    return `
      <span class="badge ${variantClass} ${sizeClass}">
        ${this.dot ? '<span class="badge-dot"></span>' : ''}
        ${this.text}
      </span>
    `;
  }

  static renderJobStatus(status) {
    const statusMap = {
      'Pending': { text: 'Pending', variant: 'gray', dot: true },
      'Planning': { text: 'Planning', variant: 'primary', dot: true },
      'Indexing': { text: 'Indexing', variant: 'primary', dot: true },
      'Completed': { text: 'Completed', variant: 'success', dot: false },
      'Failed': { text: 'Failed', variant: 'danger', dot: false },
      'Cancelled': { text: 'Cancelled', variant: 'gray', dot: false }
    };

    const config = statusMap[status] || { text: status, variant: 'default', dot: false };
    return new StatusBadge(config).render();
  }

  static renderProjectStatus(status) {
    const statusMap = {
      'NotIndexed': { text: 'Not Indexed', variant: 'gray', dot: false },
      'Indexing': { text: 'Indexing', variant: 'primary', dot: true },
      'Ready': { text: 'Ready', variant: 'success', dot: false },
      'Failed': { text: 'Failed', variant: 'danger', dot: false }
    };

    const config = statusMap[status] || { text: status, variant: 'default', dot: false };
    return new StatusBadge(config).render();
  }
}

// Add styles for StatusBadge
const style = document.createElement('style');
style.textContent = `
  .badge {
    position: relative;
  }

  .badge-sm {
    padding: 0.0625rem 0.375rem;
    font-size: 0.625rem;
  }

  .badge-lg {
    padding: 0.25rem 0.75rem;
    font-size: var(--font-size-sm);
  }

  .badge-dot {
    display: inline-block;
    width: 0.5rem;
    height: 0.5rem;
    border-radius: 50%;
    margin-right: 0.375rem;
    animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
  }

  .badge-primary .badge-dot {
    background-color: var(--color-primary-600);
  }

  .badge-success .badge-dot {
    background-color: var(--color-success-600);
  }

  .badge-warning .badge-dot {
    background-color: var(--color-warning-600);
  }

  .badge-danger .badge-dot {
    background-color: var(--color-danger-600);
  }

  .badge-gray .badge-dot {
    background-color: var(--color-gray-600);
  }

  @keyframes pulse {
    0%, 100% {
      opacity: 1;
    }
    50% {
      opacity: 0.5;
    }
  }
`;
document.head.appendChild(style);
