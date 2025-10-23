/**
 * EmptyState - Guidance component for empty lists
 *
 * Design Pattern: Never show empty screens without guidance
 * Features:
 * - Contextual illustrations and messages
 * - Clear call-to-action buttons
 * - Multiple variants for different scenarios
 * - Onboarding hints
 */
export class EmptyState {
  /**
   * Render empty state
   * @param {Object} options - Configuration
   * @returns {string} HTML string
   */
  static render(options = {}) {
    const {
      variant = 'default', // default, search, error, onboarding
      title = 'No items yet',
      description = 'Get started by creating your first item.',
      icon = null, // SVG path
      action = null, // {label, action, variant}
      secondaryAction = null,
      compact = false
    } = options;

    const iconSVG = icon || EmptyState.getDefaultIcon(variant);

    return `
      <div class="empty-state ${compact ? 'empty-state-compact' : ''}" data-empty-state>
        <div class="empty-state-content">
          <div class="empty-state-icon ${variant}">
            <svg width="${compact ? '48' : '64'}" height="${compact ? '48' : '64'}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              ${iconSVG}
            </svg>
          </div>

          <h3 class="empty-state-title">${EmptyState.escapeHtml(title)}</h3>

          ${description ? `
            <p class="empty-state-description">${EmptyState.escapeHtml(description)}</p>
          ` : ''}

          ${action || secondaryAction ? `
            <div class="empty-state-actions">
              ${action ? `
                <button class="btn btn-${action.variant || 'primary'}"
                        data-action="${EmptyState.escapeHtml(action.action)}">
                  ${action.icon ? `
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      ${action.icon}
                    </svg>
                  ` : ''}
                  <span>${EmptyState.escapeHtml(action.label)}</span>
                </button>
              ` : ''}

              ${secondaryAction ? `
                <button class="btn btn-${secondaryAction.variant || 'secondary'}"
                        data-action="${EmptyState.escapeHtml(secondaryAction.action)}">
                  ${secondaryAction.icon ? `
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      ${secondaryAction.icon}
                    </svg>
                  ` : ''}
                  <span>${EmptyState.escapeHtml(secondaryAction.label)}</span>
                </button>
              ` : ''}
            </div>
          ` : ''}
        </div>
      </div>
    `;
  }

  /**
   * Render for specific contexts
   */
  static forAnalyses() {
    return EmptyState.render({
      variant: 'onboarding',
      title: 'No analyses yet',
      description: 'Create your first analysis to start extracting insights from documents.',
      icon: '<path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><polyline points="13 2 13 9 20 9"></polyline>',
      action: {
        label: 'Create Analysis',
        action: 'create-analysis',
        variant: 'primary',
        icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
      },
      secondaryAction: {
        label: 'View Guide',
        action: 'view-guide',
        variant: 'secondary'
      }
    });
  }

  static forAnalysisTypes() {
    return EmptyState.render({
      variant: 'onboarding',
      title: 'No analysis types defined',
      description: 'Analysis types define what insights to extract from documents. Create your first type or use AI to generate one.',
      icon: '<rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><line x1="12" y1="8" x2="12" y2="16"></line><line x1="8" y1="12" x2="16" y2="12"></line>',
      action: {
        label: 'Create Type',
        action: 'create-analysis-type',
        variant: 'primary',
        icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
      },
      secondaryAction: {
        label: 'AI Generate',
        action: 'ai-generate-type',
        variant: 'secondary',
        icon: '<circle cx="12" cy="12" r="3"></circle><path d="M12 1v6m0 6v6"></path>'
      }
    });
  }

  static forSourceTypes() {
    return EmptyState.render({
      variant: 'onboarding',
      title: 'No source types defined',
      description: 'Source types define how to classify and process different document types.',
      icon: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline>',
      action: {
        label: 'Create Type',
        action: 'create-source-type',
        variant: 'primary',
        icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
      }
    });
  }

  static forDocuments() {
    return EmptyState.render({
      title: 'No documents uploaded',
      description: 'Upload documents to begin analysis.',
      icon: '<path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><polyline points="13 2 13 9 20 9"></polyline>',
      action: {
        label: 'Upload Documents',
        action: 'upload-documents',
        variant: 'primary',
        icon: '<polyline points="16 16 12 12 8 16"></polyline><line x1="12" y1="12" x2="12" y2="21"></line>'
      }
    });
  }

  static forSearchResults() {
    return EmptyState.render({
      variant: 'search',
      title: 'No results found',
      description: 'Try adjusting your search terms or filters.',
      icon: '<circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>',
      compact: true
    });
  }

  static forError(errorMessage = 'Something went wrong') {
    return EmptyState.render({
      variant: 'error',
      title: 'Unable to load data',
      description: errorMessage,
      icon: '<circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line>',
      action: {
        label: 'Try Again',
        action: 'retry',
        variant: 'primary'
      }
    });
  }

  /**
   * Get default icon for variant
   */
  static getDefaultIcon(variant) {
    const icons = {
      default: '<circle cx="12" cy="12" r="10"></circle><line x1="8" y1="12" x2="16" y2="12"></line><line x1="12" y1="8" x2="12" y2="16"></line>',
      search: '<circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>',
      error: '<circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line>',
      onboarding: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>'
    };
    return icons[variant] || icons.default;
  }

  /**
   * Attach event handlers
   */
  static attachEventHandlers(container, eventBus) {
    if (!container) return;

    const emptyState = container.querySelector('[data-empty-state]');
    if (!emptyState) return;

    const actionButtons = emptyState.querySelectorAll('[data-action]');
    actionButtons.forEach(button => {
      button.addEventListener('click', (e) => {
        e.preventDefault();
        const action = button.getAttribute('data-action');
        eventBus.emit('empty-state-action', action);
      });
    });
  }

  /**
   * Escape HTML
   */
  static escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = String(text);
    return div.innerHTML;
  }
}
