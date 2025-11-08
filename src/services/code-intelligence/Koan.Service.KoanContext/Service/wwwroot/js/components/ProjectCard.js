/**
 * ProjectCard Component
 * Displays project information with status, document count, and actions
 */
export class ProjectCard {
  constructor(options) {
    this.project = options.project;
    this.onIndex = options.onIndex;
    this.onDelete = options.onDelete;
    this.onView = options.onView;
  }

  render() {
    const { project } = this;
    const statusBadge = this.getStatusBadge();
    const lastIndexedText = this.formatLastIndexed(project.lastIndexed);

    return `
      <div class="card project-card" data-project-id="${project.id}">
        <div class="card-header">
          <div class="flex justify-between items-start">
            <div class="flex-1">
              <h3 class="card-title">${this.escapeHtml(project.name)}</h3>
              <p class="text-small text-secondary mt-1">${this.escapeHtml(project.rootPath)}</p>
            </div>
            ${statusBadge}
          </div>
        </div>

        <div class="card-body">
          <!-- Project Stats -->
          <div class="project-stats mb-4">
            <div class="stat-item">
              <span class="stat-label">Documents:</span>
              <span class="stat-value">${project.documentCount || 0}</span>
            </div>
            <div class="stat-item">
              <span class="stat-label">Size:</span>
              <span class="stat-value">${this.formatBytes(project.indexedBytes)}</span>
            </div>
            ${project.commitSha ? `
              <div class="stat-item">
                <span class="stat-label">Commit:</span>
                <span class="stat-value"><code>${project.commitSha.substring(0, 7)}</code></span>
              </div>
            ` : ''}
          </div>

          <!-- Last Indexed -->
          <div class="text-xs text-tertiary mb-3">
            ${lastIndexedText}
          </div>

          <!-- Error Message -->
          ${project.lastError ? `
            <div class="alert alert-danger mb-3">
              ${this.escapeHtml(project.lastError)}
            </div>
          ` : ''}
        </div>

        <div class="card-footer">
          <div class="btn-group flex gap-2">
            ${this.renderIndexButton()}
            ${this.onView ? `
              <button class="btn btn-secondary btn-sm project-view-btn" data-project-id="${project.id}">
                View
              </button>
            ` : ''}
            ${this.onDelete ? `
              <button class="btn btn-ghost btn-sm project-delete-btn" data-project-id="${project.id}">
                Delete
              </button>
            ` : ''}
          </div>
        </div>
      </div>
    `;
  }

  getStatusBadge() {
    const { status } = this.project;
    const badges = {
      'NotIndexed': '<span class="badge badge-gray">Not Indexed</span>',
      'Indexing': '<span class="badge badge-primary">Indexing...</span>',
      'Ready': '<span class="badge badge-success">Ready</span>',
      'Failed': '<span class="badge badge-danger">Failed</span>'
    };
    return badges[status] || `<span class="badge">${status}</span>`;
  }

  renderIndexButton() {
    const { status } = this.project;
    const isIndexing = status === 'Indexing';
    const buttonText = isIndexing ? 'Indexing...' : 'Index';
    const buttonClass = isIndexing ? 'btn-secondary disabled' : 'btn-primary';

    if (this.onIndex) {
      return `
        <button class="btn ${buttonClass} btn-sm project-index-btn"
                data-project-id="${this.project.id}"
                ${isIndexing ? 'disabled' : ''}>
          ${buttonText}
        </button>
      `;
    }
    return '';
  }

  formatLastIndexed(isoString) {
    if (!isoString) {
      return 'Never indexed';
    }

    const date = new Date(isoString);
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) {
      return 'Last indexed just now';
    } else if (diffMins < 60) {
      return `Last indexed ${diffMins} minute${diffMins > 1 ? 's' : ''} ago`;
    } else if (diffHours < 24) {
      return `Last indexed ${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
    } else if (diffDays < 7) {
      return `Last indexed ${diffDays} day${diffDays > 1 ? 's' : ''} ago`;
    } else {
      return `Last indexed on ${date.toLocaleDateString()}`;
    }
  }

  formatBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';

    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));

    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
  }

  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }
}

// Add styles for ProjectCard
const style = document.createElement('style');
style.textContent = `
  .project-card {
    transition: transform var(--transition-fast), box-shadow var(--transition-fast);
  }

  .project-card:hover {
    transform: translateY(-2px);
    box-shadow: var(--shadow-lg);
  }

  .project-stats {
    display: flex;
    flex-direction: column;
    gap: var(--spacing-2);
  }

  .stat-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: var(--spacing-2);
    background-color: var(--color-background-secondary);
    border-radius: var(--border-radius-sm);
  }

  .stat-label {
    font-size: var(--font-size-sm);
    color: var(--color-text-secondary);
  }

  .stat-value {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-semibold);
    color: var(--color-text-primary);
  }

  .btn-group {
    display: flex;
    flex-wrap: wrap;
  }
`;
document.head.appendChild(style);
