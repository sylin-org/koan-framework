/**
 * Dashboard - Strategic entry point with holistic view
 * Inspired by GDoc Assistant's dashboard pattern
 */
export class Dashboard {
  constructor(api, state, eventBus) {
    this.api = api;
    this.state = state;
    this.eventBus = eventBus;
    this.favorites = this.loadFavorites();
  }

  /**
   * Load favorites from localStorage
   */
  loadFavorites() {
    try {
      const stored = localStorage.getItem('meridian-favorites');
      return stored ? JSON.parse(stored) : { types: [], analyses: [] };
    } catch (e) {
      console.error('Failed to load favorites:', e);
      return { types: [], analyses: [] };
    }
  }

  /**
   * Save favorites to localStorage
   */
  saveFavorites() {
    try {
      localStorage.setItem('meridian-favorites', JSON.stringify(this.favorites));
    } catch (e) {
      console.error('Failed to save favorites:', e);
    }
  }

  /**
   * Toggle favorite status
   */
  toggleFavorite(type, id) {
    const key = type === 'type' ? 'types' : 'analyses';
    const index = this.favorites[key].indexOf(id);

    if (index === -1) {
      this.favorites[key].push(id);
    } else {
      this.favorites[key].splice(index, 1);
    }

    this.saveFavorites();
    this.eventBus.emit('favorites-changed', this.favorites);
    return this.favorites[key].includes(id);
  }

  /**
   * Check if item is favorited
   */
  isFavorite(type, id) {
    const key = type === 'type' ? 'types' : 'analyses';
    return this.favorites[key].includes(id);
  }

  /**
   * Render the dashboard
   */
  async render() {
    try {
      // Fetch data for metrics
      const [types, sourceTypes, pipelines] = await Promise.all([
        this.api.getAnalysisTypes(),
        this.api.getSourceTypes(),
        this.api.getPipelines()
      ]);

      // Calculate metrics
      const totalDocuments = pipelines.reduce((sum, p) => sum + (p.documentCount || 0), 0);
      const processingJobs = pipelines.filter(p => p.status === 'Processing').length;
      const completedAnalyses = pipelines.filter(p => p.status === 'Completed').length;

      return `
        <div class="dashboard">
          <div class="dashboard-content">
            <div class="dashboard-main">
              ${this.renderSystemOverview(types.length, sourceTypes.length, pipelines.length, totalDocuments, processingJobs, completedAnalyses)}
              ${this.renderRecentActivity(pipelines)}
              ${this.renderFavorites(types, pipelines)}
            </div>

            <div class="dashboard-sidebar">
              ${this.renderQuickActions()}
            </div>
          </div>
        </div>
      `;
    } catch (error) {
      console.error('Failed to render dashboard:', error);
      return `
        <div class="dashboard-error">
          <h2>Failed to load dashboard</h2>
          <p>${error.message}</p>
        </div>
      `;
    }
  }

  /**
   * Render hero section with value proposition
   */
  renderHero() {
    return `
      <div class="dashboard-hero">
        <div class="hero-content">
          <h1 class="hero-title">Living Intelligence Workspace</h1>
          <p class="hero-subtitle">
            Continuous document analysis with authoritative oversight.
            Upload documents, define analysis goals, and watch insights evolve.
          </p>
        </div>
        <div class="hero-actions">
          <button class="btn btn-primary btn-press" data-action="new-analysis">
            <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
            New Analysis
          </button>
          <button class="btn btn-secondary btn-press" data-action="new-type-ai">
            <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="3"></circle>
              <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
            </svg>
            AI Create Type
          </button>
        </div>
      </div>
    `;
  }

  /**
   * Render quick actions sidebar
   */
  renderQuickActions() {
    return `
      <div class="quick-actions">
        <h3 class="quick-actions-title">Quick Actions</h3>
        <div class="quick-actions-list">
          <button class="quick-action-item hover-scale" data-action="new-analysis">
            <div class="quick-action-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
              </svg>
            </div>
            <div class="quick-action-content">
              <div class="quick-action-label">New Analysis</div>
              <div class="quick-action-desc">Start analyzing documents</div>
            </div>
          </button>

          <button class="quick-action-item hover-scale" data-action="new-type">
            <div class="quick-action-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                <line x1="12" y1="8" x2="12" y2="16"></line>
                <line x1="8" y1="12" x2="16" y2="12"></line>
              </svg>
            </div>
            <div class="quick-action-content">
              <div class="quick-action-label">Create Type</div>
              <div class="quick-action-desc">Define analysis template</div>
            </div>
          </button>

          <button class="quick-action-item hover-scale" data-action="new-type-ai">
            <div class="quick-action-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="3"></circle>
                <path d="M12 1v6m0 6v6m8.66-13.66l-4.24 4.24m-4.84 4.84l-4.24 4.24M23 12h-6m-6 0H1m20.66 8.66l-4.24-4.24m-4.84-4.84l-4.24-4.24"></path>
              </svg>
            </div>
            <div class="quick-action-content">
              <div class="quick-action-label">AI Create Type</div>
              <div class="quick-action-desc">Generate from goal</div>
            </div>
          </button>

          <button class="quick-action-item hover-scale" data-action="manage-analysis-types">
            <div class="quick-action-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="7" height="7"></rect>
                <rect x="14" y="3" width="7" height="7"></rect>
                <rect x="14" y="14" width="7" height="7"></rect>
                <rect x="3" y="14" width="7" height="7"></rect>
              </svg>
            </div>
            <div class="quick-action-content">
              <div class="quick-action-label">Manage Analysis Types</div>
              <div class="quick-action-desc">View all analysis types</div>
            </div>
          </button>

          <button class="quick-action-item hover-scale" data-action="manage-source-types">
            <div class="quick-action-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
                <line x1="12" y1="11" x2="12" y2="17"></line>
                <polyline points="9 14 12 17 15 14"></polyline>
              </svg>
            </div>
            <div class="quick-action-content">
              <div class="quick-action-label">Manage Source Types</div>
              <div class="quick-action-desc">Define input schemas</div>
            </div>
          </button>

          <button class="quick-action-item hover-scale" data-action="view-analyses">
            <div class="quick-action-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path>
                <polyline points="13 2 13 9 20 9"></polyline>
              </svg>
            </div>
            <div class="quick-action-content">
              <div class="quick-action-label">View Analyses</div>
              <div class="quick-action-desc">Browse all analyses</div>
            </div>
          </button>
        </div>
      </div>
    `;
  }

  /**
   * Render system overview metrics
   */
  renderSystemOverview(analysisTypesCount, sourceTypesCount, analysesCount, documentsCount, processingCount, completedCount) {
    const activeRate = analysesCount > 0 ? Math.round((processingCount / analysesCount) * 100) : 0;

    return `
      <div class="system-overview">
        <h2 class="section-title">System Overview</h2>
        <div class="metrics-grid">
          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                <line x1="12" y1="8" x2="12" y2="16"></line>
                <line x1="8" y1="12" x2="16" y2="12"></line>
              </svg>
            </div>
            <div class="metric-value">${analysisTypesCount}</div>
            <div class="metric-label">Analysis Types</div>
            <div class="metric-change">
              <button class="metric-action btn-press" data-action="manage-analysis-types">View All →</button>
            </div>
          </div>

          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
                <line x1="12" y1="11" x2="12" y2="17"></line>
                <polyline points="9 14 12 17 15 14"></polyline>
              </svg>
            </div>
            <div class="metric-value">${sourceTypesCount}</div>
            <div class="metric-label">Source Types</div>
            <div class="metric-change">
              <button class="metric-action btn-press" data-action="manage-source-types">View All →</button>
            </div>
          </div>

          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path>
                <polyline points="13 2 13 9 20 9"></polyline>
              </svg>
            </div>
            <div class="metric-value">${analysesCount}</div>
            <div class="metric-label">Active Analyses</div>
            <div class="metric-change">
              <button class="metric-action btn-press" data-action="view-analyses">View All →</button>
            </div>
          </div>

          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
              </svg>
            </div>
            <div class="metric-value">${documentsCount}</div>
            <div class="metric-label">Documents Processed</div>
          </div>

          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"></circle>
                <polyline points="12 6 12 12 16 14"></polyline>
              </svg>
            </div>
            <div class="metric-value">${processingCount}</div>
            <div class="metric-label">Currently Processing</div>
          </div>

          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="20 6 9 17 4 12"></polyline>
              </svg>
            </div>
            <div class="metric-value">${completedCount}</div>
            <div class="metric-label">Completed</div>
          </div>

          <div class="metric-card">
            <div class="metric-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 12h-4l-3 9L9 3l-3 9H2"></path>
              </svg>
            </div>
            <div class="metric-value">${activeRate}%</div>
            <div class="metric-label">Activity Rate</div>
          </div>
        </div>
      </div>
    `;
  }

  /**
   * Render recent activity stream
   */
  renderRecentActivity(pipelines) {
    const recent = pipelines
      .sort((a, b) => new Date(b.updatedAt || b.createdAt) - new Date(a.updatedAt || a.createdAt))
      .slice(0, 10);

    if (recent.length === 0) {
      return `
        <div class="recent-activity">
          <h2 class="section-title">Recent Activity</h2>
          <div class="empty-state">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
              <circle cx="12" cy="12" r="10"></circle>
              <line x1="12" y1="8" x2="12" y2="12"></line>
              <line x1="12" y1="16" x2="12.01" y2="16"></line>
            </svg>
            <p>No recent activity</p>
            <button class="btn btn-primary btn-press" data-action="new-analysis">Create Your First Analysis</button>
          </div>
        </div>
      `;
    }

    return `
      <div class="recent-activity">
        <h2 class="section-title">Recent Activity</h2>
        <div class="activity-list">
          ${recent.map(pipeline => this.renderActivityItem(pipeline)).join('')}
        </div>
      </div>
    `;
  }

  /**
   * Render activity item
   */
  renderActivityItem(pipeline) {
    const status = pipeline.status || 'Unknown';
    const statusClass = status.toLowerCase();
    const timestamp = this.formatTimestamp(pipeline.updatedAt || pipeline.createdAt);
    const docCount = pipeline.documentCount || 0;

    return `
      <div class="activity-item" data-pipeline-id="${pipeline.id}">
        <div class="activity-icon">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path>
            <polyline points="13 2 13 9 20 9"></polyline>
          </svg>
        </div>
        <div class="activity-content">
          <div class="activity-header">
            <span class="activity-name">${this.escapeHtml(pipeline.name)}</span>
            <span class="badge badge-${statusClass}">${status}</span>
          </div>
          <div class="activity-meta">
            <span class="activity-type">${this.escapeHtml(pipeline.analysisType || 'Unknown Type')}</span>
            <span class="activity-separator">•</span>
            <span class="activity-docs">${docCount} ${docCount === 1 ? 'document' : 'documents'}</span>
            <span class="activity-separator">•</span>
            <span class="activity-time">${timestamp}</span>
          </div>
        </div>
        <div class="activity-actions">
          <button class="btn-icon" data-action="open-analysis" data-pipeline-id="${pipeline.id}" title="Open analysis">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="9 18 15 12 9 6"></polyline>
            </svg>
          </button>
          <button class="btn-icon ${this.isFavorite('analysis', pipeline.id) ? 'active' : ''}"
                  data-action="toggle-favorite-analysis"
                  data-pipeline-id="${pipeline.id}"
                  title="Toggle favorite">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="${this.isFavorite('analysis', pipeline.id) ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="2">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
            </svg>
          </button>
        </div>
      </div>
    `;
  }

  /**
   * Render favorites section
   */
  renderFavorites(types, pipelines) {
    const favoriteTypes = types.filter(t => this.isFavorite('type', t.id));
    const favoriteAnalyses = pipelines.filter(p => this.isFavorite('analysis', p.id));

    if (favoriteTypes.length === 0 && favoriteAnalyses.length === 0) {
      return `
        <div class="favorites">
          <h2 class="section-title">Favorites</h2>
          <div class="empty-state">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
            </svg>
            <p>No favorites yet</p>
            <p class="empty-state-hint">Star items to keep them handy</p>
          </div>
        </div>
      `;
    }

    return `
      <div class="favorites">
        <h2 class="section-title">Favorites</h2>

        ${favoriteTypes.length > 0 ? `
          <div class="favorites-section">
            <h3 class="favorites-section-title">Analysis Types</h3>
            <div class="favorites-grid">
              ${favoriteTypes.map(type => this.renderFavoriteTypeCard(type)).join('')}
            </div>
          </div>
        ` : ''}

        ${favoriteAnalyses.length > 0 ? `
          <div class="favorites-section">
            <h3 class="favorites-section-title">Analyses</h3>
            <div class="favorites-list">
              ${favoriteAnalyses.map(pipeline => this.renderActivityItem(pipeline)).join('')}
            </div>
          </div>
        ` : ''}
      </div>
    `;
  }

  /**
   * Render favorite type card
   */
  renderFavoriteTypeCard(type) {
    return `
      <div class="favorite-type-card" data-type-id="${type.id}">
        <div class="favorite-type-header">
          <span class="favorite-type-name">${this.escapeHtml(type.name)}</span>
          <button class="btn-icon active"
                  data-action="toggle-favorite-type"
                  data-type-id="${type.id}"
                  title="Remove from favorites">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-width="2">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>
            </svg>
          </button>
        </div>
        <p class="favorite-type-desc">${this.escapeHtml(type.description || 'No description')}</p>
        <button class="btn btn-secondary btn-sm" data-action="use-type" data-type-id="${type.id}">
          Use This Type
        </button>
      </div>
    `;
  }

  /**
   * Format timestamp
   */
  formatTimestamp(timestamp) {
    if (!timestamp) return 'Unknown';

    const date = new Date(timestamp);
    const now = new Date();
    const diff = now - date;

    // Less than 1 minute
    if (diff < 60000) return 'Just now';

    // Less than 1 hour
    if (diff < 3600000) {
      const minutes = Math.floor(diff / 60000);
      return `${minutes}m ago`;
    }

    // Less than 24 hours
    if (diff < 86400000) {
      const hours = Math.floor(diff / 3600000);
      return `${hours}h ago`;
    }

    // Less than 7 days
    if (diff < 604800000) {
      const days = Math.floor(diff / 86400000);
      return `${days}d ago`;
    }

    // Format as date
    return date.toLocaleDateString();
  }

  /**
   * Escape HTML
   */
  escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  /**
   * Attach event handlers
   */
  attachEventHandlers(container) {
    // Navigation actions
    container.addEventListener('click', (e) => {
      const target = e.target.closest('[data-action]');
      if (!target) return;

      const action = target.dataset.action;
      const pipelineId = target.dataset.pipelineId;
      const typeId = target.dataset.typeId;

      switch (action) {
        case 'new-analysis':
          this.eventBus.emit('navigate', 'new-analysis');
          break;
        case 'new-type':
          this.eventBus.emit('navigate', 'new-type');
          break;
        case 'new-type-ai':
          this.eventBus.emit('navigate', 'new-type-ai');
          break;
        case 'manage-types':
          this.eventBus.emit('navigate', 'manage-types');
          break;
        case 'manage-analysis-types':
          this.eventBus.emit('navigate', 'analysis-types-list');
          break;
        case 'manage-source-types':
          this.eventBus.emit('navigate', 'source-types-list');
          break;
        case 'view-analyses':
          this.eventBus.emit('navigate', 'analyses-list');
          break;
        case 'open-analysis':
          if (pipelineId) {
            this.eventBus.emit('navigate', 'analysis-workspace', { pipelineId });
          }
          break;
        case 'use-type':
          if (typeId) {
            this.eventBus.emit('navigate', 'new-analysis', { typeId });
          }
          break;
        case 'toggle-favorite-analysis':
          if (pipelineId) {
            const isFavorite = this.toggleFavorite('analysis', pipelineId);
            target.classList.toggle('active', isFavorite);
            const svg = target.querySelector('svg');
            if (svg) {
              svg.setAttribute('fill', isFavorite ? 'currentColor' : 'none');
            }
          }
          break;
        case 'toggle-favorite-type':
          if (typeId) {
            const isFavorite = this.toggleFavorite('type', typeId);
            target.classList.toggle('active', isFavorite);
            const svg = target.querySelector('svg');
            if (svg) {
              svg.setAttribute('fill', isFavorite ? 'currentColor' : 'none');
            }
            // Re-render favorites section if needed
            this.eventBus.emit('favorites-changed');
          }
          break;
      }
    });
  }
}
