/**
 * Process Monitor Component
 * Floating progress card for background photo processing.
 *
 * Transport (SnapVault D4): a browser-native EventSource over GET /api/photos/progress/{batchId} — the SSE
 * replacement for the old SignalR hub. No client library, no hub connection, no subscribe/unsubscribe: the
 * server streams a `PhotoProgress` frame per photo and a terminal `JobCompleted` frame (a read-projection of
 * the durable jobs ledger), then closes. We close the EventSource on completion so it does not auto-reconnect.
 */

import { escapeHtml } from '../utils/html.js';
import { PhotoSetManager } from '../services/PhotoSetManager.js';

export class ProcessMonitor {
  constructor(app) {
    this.app = app;
    this.currentJob = null;
    this.isExpanded = false;
    this.isMinimized = false;
    this.photoProgress = new Map();
    this.eventSource = null;
    this.render();
  }

  render() {
    const monitor = document.createElement('div');
    monitor.className = 'process-monitor';
    monitor.innerHTML = `
      <!-- Minimized State -->
      <div class="monitor-minimized">
        <button class="btn-restore" aria-label="Show upload progress">
          <div class="spinner-icon">⚙️</div>
          <span class="badge-count">0</span>
        </button>
      </div>

      <!-- Compact/Expanded States -->
      <div class="monitor-card">
        <!-- Header -->
        <div class="monitor-header">
          <div class="monitor-title">
            <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="17 8 12 3 7 8"></polyline>
              <line x1="12" y1="3" x2="12" y2="15"></line>
            </svg>
            <span class="title-text">Upload Progress</span>
          </div>
          <div class="monitor-actions">
            <button class="btn-toggle" aria-label="Toggle details" title="Toggle details">
              <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="6 9 12 15 18 9"></polyline>
              </svg>
            </button>
            <button class="btn-minimize" aria-label="Minimize" title="Minimize">
              <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="5" y1="12" x2="19" y2="12"></line>
              </svg>
            </button>
            <button class="btn-close-monitor" aria-label="Close" title="Close (processing continues)">
              <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>
        </div>

        <!-- Compact Summary -->
        <div class="monitor-summary">
          <div class="summary-text">
            <span class="summary-status">Processing <span class="completed-count">0</span> of <span class="total-count">0</span> photos</span>
          </div>
          <div class="progress-bar-container">
            <div class="progress-bar-fill" style="width: 0%"></div>
          </div>
          <div class="progress-percentage">0%</div>
        </div>

        <!-- Expanded Details -->
        <div class="monitor-details">
          <div class="details-list"></div>
        </div>
      </div>
    `;

    document.body.appendChild(monitor);
    this.container = monitor;

    this.setupEventListeners();
  }

  setupEventListeners() {
    // Toggle expand/collapse
    const toggleBtn = this.container.querySelector('.btn-toggle');
    toggleBtn.addEventListener('click', () => this.toggleExpanded());

    // Minimize
    const minimizeBtn = this.container.querySelector('.btn-minimize');
    minimizeBtn.addEventListener('click', () => this.minimize());

    // Restore from minimized
    const restoreBtn = this.container.querySelector('.btn-restore');
    restoreBtn.addEventListener('click', () => this.restore());

    // Close
    const closeBtn = this.container.querySelector('.btn-close-monitor');
    closeBtn.addEventListener('click', () => this.hide());
  }

  startJob(batchId, totalFiles) {
    this.currentJob = {
      id: batchId,
      totalFiles: totalFiles,
      completedCount: 0,
      failedCount: 0
    };

    this.photoProgress.clear();
    this.show();

    // Update UI
    this.container.querySelector('.total-count').textContent = totalFiles;
    this.container.querySelector('.completed-count').textContent = '0';
    this.container.querySelector('.badge-count').textContent = totalFiles;

    // Open the SSE progress stream for this batch.
    this.connectToProgress(batchId);
  }

  connectToProgress(batchId) {
    try {
      // Native SSE — same-origin, so the auth cookie (and thus the tenant/subject ambient) rides automatically.
      const source = new EventSource(`/api/photos/progress/${encodeURIComponent(batchId)}`);

      source.addEventListener('PhotoProgress', (e) => this.handlePhotoProgress(JSON.parse(e.data)));
      source.addEventListener('JobCompleted', (e) => this.handleJobCompleted(JSON.parse(e.data)));

      // EventSource auto-reconnects on transient drops (its job). A hard error while we have no live job means
      // the stream is done/unavailable — surface it once and stop reconnecting.
      source.onerror = () => {
        if (source.readyState === EventSource.CLOSED) {
          this.disconnect();
        }
      };

      this.eventSource = source;
    } catch (error) {
      console.error('SSE progress connection failed:', error);
      this.app.components.toast.show('Real-time updates unavailable', { icon: '⚠️', duration: 3000 });
    }
  }

  handlePhotoProgress(event) {
    // Key on the stable work-item id (unique per file even before a photo id exists) so two identically-named
    // uploads don't collapse into one row; fall back for resilience if the field is ever absent.
    const key = event.workItemId || event.photoId || event.fileName;
    this.photoProgress.set(key, {
      fileName: event.fileName,
      status: event.status,
      stage: event.stage,
      error: event.error
    });
    this.updateUI();
  }

  handleJobCompleted(event) {
    this.app.components.toast.show(
      `Processing complete: ${event.successCount} succeeded, ${event.failureCount} failed`,
      { icon: '✅', duration: 5000 }
    );

    // Show completion state
    this.showCompletionState(event);

    // Invalidate all-photos cache (new photos uploaded)
    PhotoSetManager.invalidateCache('all-photos');

    // Reload current view to show newly processed photos
    this.app.components.collectionView.loadPhotos();

    // Close the stream (prevents EventSource auto-reconnect after the server ends it).
    this.disconnect();

    // Auto-hide after 3 seconds
    setTimeout(() => {
      if (this.container.classList.contains('visible')) {
        this.hide();
      }
    }, 3000);
  }

  updateUI() {
    if (!this.currentJob) return;

    // Count completed photos
    let completedCount = 0;
    this.photoProgress.forEach((progress) => {
      if (progress.status === 'completed') {
        completedCount++;
      }
    });

    this.currentJob.completedCount = completedCount;

    // Update summary
    const completedSpan = this.container.querySelector('.completed-count');
    const percentage = Math.round((completedCount / this.currentJob.totalFiles) * 100);

    completedSpan.textContent = completedCount;
    this.container.querySelector('.progress-bar-fill').style.width = `${percentage}%`;
    this.container.querySelector('.progress-percentage').textContent = `${percentage}%`;

    // Update minimized badge
    const remaining = this.currentJob.totalFiles - completedCount;
    this.container.querySelector('.badge-count').textContent = remaining;

    // Update expanded details list
    if (this.isExpanded) {
      this.renderDetailsList();
    }
  }

  renderDetailsList() {
    const detailsList = this.container.querySelector('.details-list');
    const items = [];

    this.photoProgress.forEach((progress, photoId) => {
      const statusIcon = this.getStatusIcon(progress.status);
      const stageText = this.getStageText(progress.stage);
      const statusClass = progress.status === 'failed' ? 'detail-item-error' : '';

      items.push(`
        <div class="detail-item ${statusClass}">
          <span class="detail-icon">${statusIcon}</span>
          <div class="detail-content">
            <div class="detail-filename">${escapeHtml(progress.fileName)}</div>
            <div class="detail-stage">${stageText}</div>
          </div>
        </div>
      `);
    });

    detailsList.innerHTML = items.join('');
  }

  showCompletionState(event) {
    const summaryStatus = this.container.querySelector('.summary-status');

    if (event.failureCount === 0) {
      summaryStatus.innerHTML = `<span style="color: var(--accent-success)">✅ All ${event.successCount} photos processed successfully</span>`;
    } else {
      summaryStatus.innerHTML = `<span style="color: var(--accent-warning)">⚠️ ${event.successCount} succeeded, ${event.failureCount} failed</span>`;
    }

    // Update progress to 100%
    this.container.querySelector('.progress-bar-fill').style.width = '100%';
    this.container.querySelector('.progress-percentage').textContent = '100%';
  }

  toggleExpanded() {
    this.isExpanded = !this.isExpanded;

    if (this.isExpanded) {
      this.container.querySelector('.monitor-card').classList.add('expanded');
      this.container.querySelector('.btn-toggle svg').style.transform = 'rotate(180deg)';
      this.renderDetailsList();
    } else {
      this.container.querySelector('.monitor-card').classList.remove('expanded');
      this.container.querySelector('.btn-toggle svg').style.transform = 'rotate(0deg)';
    }
  }

  minimize() {
    this.isMinimized = true;
    this.isExpanded = false;
    this.container.classList.add('minimized');
  }

  restore() {
    this.isMinimized = false;
    this.container.classList.remove('minimized');
  }

  show() {
    this.isExpanded = false;
    this.isMinimized = false;
    this.container.classList.add('visible');
    this.container.classList.remove('minimized');
  }

  hide() {
    this.container.classList.remove('visible', 'minimized');
    this.isExpanded = false;
    this.isMinimized = false;
    this.currentJob = null;
    this.photoProgress.clear();

    // Close the SSE stream if still open.
    this.disconnect();
  }

  disconnect() {
    if (!this.eventSource) return;
    try {
      this.eventSource.close();
    } catch (error) {
      console.debug('EventSource close error (expected if already closed):', error.message);
    }
    this.eventSource = null;
  }

  getStatusIcon(status) {
    switch (status) {
      case 'queued': return '⏳';
      case 'processing': return '⚙️';
      case 'completed': return '✅';
      case 'failed': return '❌';
      default: return '•';
    }
  }

  getStageText(stage) {
    switch (stage) {
      case 'queued': return 'Queued...';
      case 'upload': return 'Uploading...';
      case 'exif': return 'Reading EXIF...';
      case 'thumbnails': return 'Creating thumbnails...';
      case 'ai-description': return 'Analyzing with AI...';
      case 'embedding': return 'Generating embeddings...';
      case 'completed': return 'Complete';
      default: return escapeHtml(stage);   // unknown stage is a server string interpolated into innerHTML — escape it
    }
  }

}
