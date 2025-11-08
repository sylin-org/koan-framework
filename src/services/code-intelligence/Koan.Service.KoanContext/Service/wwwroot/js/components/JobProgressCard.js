/**
 * JobProgressCard Component
 * Displays real-time job progress with status, files processed, and ETA
 */
export class JobProgressCard {
  constructor(options) {
    this.job = options.job;
    this.onCancel = options.onCancel;
  }

  render() {
    const { job } = this;
    const statusBadge = this.getStatusBadge();
    const progressPercent = Math.round(job.progress * 100);

    return `
      <div class="card job-progress-card" data-job-id="${job.jobId}">
        <div class="card-header">
          <div class="flex justify-between items-center">
            <div>
              <h4 class="card-title mb-1">${job.projectId || 'Indexing Job'}</h4>
              ${statusBadge}
            </div>
            ${this.renderActions()}
          </div>
        </div>

        <div class="card-body">
          <!-- Progress Bar -->
          <div class="progress-container mb-4">
            <div class="progress-bar">
              <div class="progress-fill" style="width: ${progressPercent}%"></div>
            </div>
            <div class="progress-label text-small text-secondary mt-1">
              ${progressPercent}% complete
            </div>
          </div>

          <!-- Job Stats Grid -->
          <div class="job-stats-grid">
            <div class="job-stat">
              <div class="text-xs text-tertiary">Files</div>
              <div class="text-base font-semibold">${job.processedFiles || 0} / ${job.totalFiles || 0}</div>
            </div>
            <div class="job-stat">
              <div class="text-xs text-tertiary">Chunks</div>
              <div class="text-base font-semibold">${job.chunksCreated || 0}</div>
            </div>
            <div class="job-stat">
              <div class="text-xs text-tertiary">Vectors</div>
              <div class="text-base font-semibold">${job.vectorsSaved || 0}</div>
            </div>
            <div class="job-stat">
              <div class="text-xs text-tertiary">Elapsed</div>
              <div class="text-base font-semibold">${this.formatDuration(job.elapsed)}</div>
            </div>
          </div>

          <!-- Current Operation -->
          ${job.currentOperation ? `
            <div class="current-operation mt-4">
              <div class="text-xs text-tertiary mb-1">Current Operation</div>
              <div class="text-sm">${job.currentOperation}</div>
            </div>
          ` : ''}

          <!-- ETA -->
          ${job.estimatedCompletion ? `
            <div class="eta mt-3 text-sm text-secondary">
              Estimated completion: ${this.formatETA(job.estimatedCompletion)}
            </div>
          ` : ''}

          <!-- Error Message -->
          ${job.errorMessage ? `
            <div class="alert alert-danger mt-4">
              <strong>Error:</strong> ${job.errorMessage}
            </div>
          ` : ''}
        </div>
      </div>
    `;
  }

  getStatusBadge() {
    const { status } = this.job;
    const badges = {
      'Pending': '<span class="badge badge-gray">Pending</span>',
      'Planning': '<span class="badge badge-primary">Planning</span>',
      'Indexing': '<span class="badge badge-primary">Indexing</span>',
      'Completed': '<span class="badge badge-success">Completed</span>',
      'Failed': '<span class="badge badge-danger">Failed</span>',
      'Cancelled': '<span class="badge badge-gray">Cancelled</span>'
    };
    return badges[status] || `<span class="badge">${status}</span>`;
  }

  renderActions() {
    const { status } = this.job;
    const canCancel = status === 'Pending' || status === 'Planning' || status === 'Indexing';

    if (canCancel && this.onCancel) {
      return `
        <button class="btn btn-sm btn-ghost job-cancel-btn" data-job-id="${this.job.jobId}">
          Cancel
        </button>
      `;
    }
    return '';
  }

  formatDuration(seconds) {
    if (!seconds) return '0s';

    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);

    if (hours > 0) {
      return `${hours}h ${minutes}m`;
    } else if (minutes > 0) {
      return `${minutes}m ${secs}s`;
    } else {
      return `${secs}s`;
    }
  }

  formatETA(isoString) {
    if (!isoString) return 'Unknown';

    const eta = new Date(isoString);
    const now = new Date();
    const diffMs = eta - now;

    if (diffMs < 0) return 'Soon';

    const diffSecs = Math.floor(diffMs / 1000);
    return `in ${this.formatDuration(diffSecs)}`;
  }
}

// Add styles for JobProgressCard
const style = document.createElement('style');
style.textContent = `
  .job-progress-card {
    border-left: 4px solid var(--color-primary-500);
  }

  .progress-container {
    width: 100%;
  }

  .progress-bar {
    width: 100%;
    height: 8px;
    background-color: var(--color-background-tertiary);
    border-radius: var(--border-radius-full);
    overflow: hidden;
  }

  .progress-fill {
    height: 100%;
    background: linear-gradient(90deg, var(--color-primary-500), var(--color-primary-600));
    border-radius: var(--border-radius-full);
    transition: width var(--transition-slow);
  }

  .job-stats-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(100px, 1fr));
    gap: var(--spacing-4);
  }

  .job-stat {
    text-align: center;
    padding: var(--spacing-3);
    background-color: var(--color-background-secondary);
    border-radius: var(--border-radius-md);
  }

  .current-operation {
    padding: var(--spacing-3);
    background-color: var(--color-background-secondary);
    border-radius: var(--border-radius-md);
    border-left: 3px solid var(--color-primary-500);
  }

  .eta {
    font-style: italic;
  }

  .job-cancel-btn:hover {
    background-color: var(--color-danger-50);
    color: var(--color-danger-700);
  }
`;
document.head.appendChild(style);
