/**
 * EmptyState Component
 * Displays when there's no data to show
 */
export class EmptyState {
  constructor(options) {
    this.title = options.title || 'No data';
    this.message = options.message || 'There is no data to display yet.';
    this.icon = options.icon || '📭';
    this.actionText = options.actionText;
    this.onAction = options.onAction;
    this.actionId = options.actionId || 'empty-state-action';
  }

  render() {
    return `
      <div class="empty-state">
        <div class="empty-state-content">
          <div class="empty-state-icon">${this.icon}</div>
          <h3 class="empty-state-title">${this.title}</h3>
          <p class="empty-state-message">${this.message}</p>
          ${this.actionText ? `
            <button class="btn btn-primary empty-state-action" id="${this.actionId}">
              ${this.actionText}
            </button>
          ` : ''}
        </div>
      </div>
    `;
  }

  static renderTo(container, options) {
    const emptyState = new EmptyState(options);
    if (typeof container === 'string') {
      container = document.querySelector(container);
    }
    if (container) {
      container.innerHTML = emptyState.render();

      if (emptyState.actionText && emptyState.onAction) {
        const actionBtn = container.querySelector(`#${emptyState.actionId}`);
        if (actionBtn) {
          actionBtn.addEventListener('click', emptyState.onAction);
        }
      }
    }
  }

  static noProjects() {
    return new EmptyState({
      title: 'No Projects Yet',
      message: 'Get started by creating your first project to index your codebase.',
      icon: '📂',
      actionText: 'Create Project',
      actionId: 'create-project-btn'
    });
  }

  static noJobs() {
    return new EmptyState({
      title: 'No Active Jobs',
      message: 'There are no indexing jobs currently running.',
      icon: '✅',
    });
  }

  static searchNoResults(query) {
    return new EmptyState({
      title: 'No Results Found',
      message: `No results found for "${query}". Try adjusting your search terms.`,
      icon: '🔍',
    });
  }
}

// Add styles for EmptyState
const style = document.createElement('style');
style.textContent = `
  .empty-state {
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 300px;
    padding: var(--spacing-8);
  }

  .empty-state-content {
    text-align: center;
    max-width: 400px;
  }

  .empty-state-icon {
    font-size: 4rem;
    margin-bottom: var(--spacing-4);
    opacity: 0.6;
  }

  .empty-state-title {
    font-size: var(--font-size-2xl);
    font-weight: var(--font-weight-semibold);
    color: var(--color-text-primary);
    margin-bottom: var(--spacing-2);
  }

  .empty-state-message {
    font-size: var(--font-size-base);
    color: var(--color-text-secondary);
    margin-bottom: var(--spacing-6);
    line-height: var(--line-height-relaxed);
  }

  .empty-state-action {
    margin-top: var(--spacing-4);
  }
`;
document.head.appendChild(style);
