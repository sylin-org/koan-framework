/**
 * Meridian Application
 * Evidence-driven living intelligence platform
 * Main entry point with Dashboard, Type Management, and Two-Column Workspace
 */

import { API } from './api.js';
import { EventBus } from './utils/EventBus.js';
import { StateManager } from './utils/StateManager.js';
import { Router } from './utils/Router.js';
import { Sidebar } from './components/Sidebar.js';
import { Toast } from './components/Toast.js';
import { TopNav } from './components/TopNav.js';
import { Dashboard } from './components/Dashboard.js';
import { TypeFormView } from './components/TypeFormView.js';
import { AnalysisTypesManager } from './components/AnalysisTypesManager.js';
import { AnalysisTypeDetailView } from './components/AnalysisTypeDetailView.js';
import { AnalysisDetailView } from './components/AnalysisDetailView.js';
import { SourceTypesManager } from './components/SourceTypesManager.js';
import { SourceTypeDetailView } from './components/SourceTypeDetailView.js';
import { SettingsSidebar } from './components/SettingsSidebar.js';
import { KeyboardShortcuts } from './components/KeyboardShortcuts.js';
import { PageHeader } from './components/PageHeader.js';
import { DetailPanel } from './components/DetailPanel.js';
import { OrganizationProfilesManager } from './components/OrganizationProfilesManager.js';

class MeridianApp {
  constructor() {
    this.api = new API();
    this.eventBus = new EventBus();
    this.router = new Router();
    this.stateManager = new StateManager({
  currentView: 'dashboard', // 'dashboard', 'analyses-list', 'manage-types'
      currentPipelineId: null,
      currentTypeId: null,
      pipelines: [],
      analysisTypes: [],
      currentPipeline: null,
      deliverable: null,
      deliverableCanonical: null,
      authoritativeNotes: null,
      notesExpanded: false,
      pipelineQuality: null,
    });
    this.toast = new Toast();
    this.appRoot = null;
    this.mainHost = null;

    // Initialize components
    this.sidebar = new Sidebar(this.router, this.eventBus);
    this.detailPanel = new DetailPanel(this.eventBus);
    this.topNav = new TopNav(this.router, this.eventBus);
    // Legacy detail panel removed; full-page routes handle view/edit.
    this.keyboardShortcuts = new KeyboardShortcuts(this.eventBus, this.router);
    this.dashboard = new Dashboard(this.api, this.stateManager, this.eventBus);
    this.analysisTypesManager = new AnalysisTypesManager(this.api, this.eventBus, this.toast, this.router);
    this.analysisTypeDetailView = new AnalysisTypeDetailView(this.api, this.eventBus, this.router, this.toast);
    this.analysisDetailView = new AnalysisDetailView(this.api, this.eventBus, this.router, this.toast);
    this.sourceTypesManager = new SourceTypesManager(this.api, this.eventBus, this.toast, this.router);
    this.sourceTypeDetailView = new SourceTypeDetailView(this.api, this.eventBus, this.toast);
  this.organizationProfilesManager = new OrganizationProfilesManager(this.api, this.eventBus, this.toast, this.router);
    // Component state
    this.state = this.stateManager.state;

    this.setupDetailPanelEvents();

    // Setup routes
    this.setupRoutes();
  }

  renderAppShell() {
    this.appRoot = document.getElementById('app');
    if (!this.appRoot) {
      console.error('App root container not found');
      return;
    }

    this.appRoot.innerHTML = `
      <div class="app-layout">
        <aside id="app-sidebar" class="app-sidebar" role="navigation" aria-label="Primary navigation"></aside>
        <div class="app-shell">
          ${this.topNav.render()}
          <div class="app-main">
            <main id="app-main" class="app-main-content" tabindex="-1">
              <div class="app-loading">
                <svg class="spinner icon-spin" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"></circle>
                </svg>
                <p>Loading Meridian...</p>
              </div>
            </main>
          </div>
        </div>
      </div>
    `;

    this.topNav.attachEventHandlers(this.appRoot);

    const sidebarHost = this.appRoot.querySelector('#app-sidebar');
    this.sidebar.mount(sidebarHost);

    this.mainHost = this.appRoot.querySelector('#app-main');
  }

  async init() {
    console.log('[Meridian] Initializing...');

    this.renderAppShell();

    // Initialize keyboard shortcuts
    this.keyboardShortcuts.init();

    // Setup navigation event listeners
    this.setupNavigation();

    // Load initial badge counts
    this.updateSidebarBadges();

    // Start router (this will load the initial route from URL)
    this.router.start((path, params) => {
      // Default handler - redirect to dashboard if no route matches
      console.warn(`No route matched for: ${path}, redirecting to dashboard`);
      this.router.navigate('', {}, true); // Replace history
    });

    console.log('[Meridian] Ready');
  }

  /**
   * Update sidebar badge counts from API
   */
  async updateSidebarBadges() {
    try {
      const [pipelines, analysisTypes, sourceTypes] = await Promise.all([
        this.api.getPipelines(),
        this.api.getAnalysisTypes(),
        this.api.getSourceTypes()
      ]);

      this.eventBus.emit('sidebar:update-badges', {
        'all-analyses': pipelines.length,
        'analysis-types': analysisTypes.length,
        'source-types': sourceTypes.length
      });
    } catch (error) {
      console.error('Failed to update sidebar badges:', error);
    }
  }

  /**
   * Setup all application routes
   */
  setupRoutes() {
    // Dashboard
    this.router.route('', (params) => this.navigate('dashboard', params));
    this.router.route('dashboard', (params) => this.navigate('dashboard', params));

    // Analyses
    this.router.route('analyses', (params) => this.navigate('analyses-list', params));
    this.router.route('analyses/create', (params) => this.navigate('analysis-create', params));
    this.router.route('analyses/:id/view', (params) => this.navigate('analysis-view', params));
    this.router.route('analyses/:id/edit', (params) => this.navigate('analysis-edit', params));
  this.router.route('analyses/:pipelineId', (params) => this.navigate('analysis-view', { id: params.pipelineId }));

    // Analysis Types
    this.router.route('analysis-types', (params) => this.navigate('analysis-types-list', params));
    this.router.route('analysis-types/create', (params) => this.navigate('analysis-type-create', params));
    this.router.route('analysis-types/:id/view', (params) => this.navigate('analysis-type-view', params));
    this.router.route('analysis-types/:id/edit', (params) => this.navigate('analysis-type-edit', params));

    // Source Types
    this.router.route('source-types', (params) => this.navigate('source-types-list', params));
    this.router.route('source-types/create', (params) => this.navigate('source-type-create', params));
    this.router.route('source-types/:id/view', (params) => this.navigate('source-type-view', params));
    this.router.route('source-types/:id/edit', (params) => this.navigate('source-type-edit', params));

  // Organization Profiles
  this.router.route('organization-profiles', (params) => this.navigate('organization-profiles', params));
  this.router.route('organization-profiles/create', (params) => this.navigate('organization-profiles', { ...params, mode: 'create' }));
  this.router.route('organization-profiles/:id/edit', (params) => this.navigate('organization-profiles', { ...params, mode: 'edit', profileId: params.id }));

    // Legacy routes (for old navigation)
    this.router.route('manage-types', (params) => this.navigate('manage-types', params));
    this.router.route('new-analysis', (params) => this.navigate('new-analysis', params));
    this.router.route('new-type', (params) => this.navigate('new-type', params));
    this.router.route('new-type-ai', (params) => this.navigate('new-type-ai', params));
  }

  setupNavigation() {
    // Listen to navigation events from components
    this.eventBus.on('navigate', (view, params = {}) => {
      // Convert view name to router path
      const routePath = this.viewToRoutePath(view, params);

      // Navigate using router (this will update URL and trigger route handler)
      const routeParams = this.filterNavigationParams(routePath, params);
      this.router.navigate(routePath, routeParams);
    });

    this.eventBus.on('toggle-sidebar', () => {
      if (this.sidebar.isOpen()) {
        this.sidebar.close();
      } else {
        this.sidebar.open();
      }
    });

    this.eventBus.on('close-sidebar', () => {
      this.sidebar.close();
    });

    this.eventBus.on('escape-pressed', () => {
      if (this.sidebar.isOpen()) {
        this.sidebar.close();
      }
    });

    // Listen for favorites changes
    this.eventBus.on('favorites-changed', () => {
      if (this.state.currentView === 'dashboard') {
        // Re-render dashboard without changing URL
        const container = document.getElementById('app');
        if (container) {
          this.renderDashboard(container);
        }
      }
    });

    // Listen for export/refresh events from insights panel
    this.eventBus.on('export-insights', () => {
      this.analysisDetailView?.handleExportReport?.();
    });

    this.eventBus.on('refresh-analysis', () => {
      if (this.analysisDetailView?.analysis?.id) {
        this.analysisDetailView.reload();
      }
    });
  }

  setupDetailPanelEvents() {
    // Redirect to workspace route for full-page experience instead of opening panel
    this.eventBus.on('detail-panel:open-workspace', (pipelineId) => {
      if (!pipelineId) {
        return;
      }
      this.router.navigate(`analyses/${pipelineId}/view`);
    });

    this.eventBus.on('detail-panel:saved', () => {
      if (this.state.currentView === 'analyses-list') {
        this.updateSidebarPipelineBadges(this.state.pipelines);
      }
    });

    this.eventBus.on('detail-panel:deleted', (pipelineId) => {
      this.updateSidebarPipelineBadges(this.state.pipelines);

      if (pipelineId && this.state.currentView === 'analysis-view' && this.state.currentPipelineId === pipelineId) {
        this.eventBus.emit('navigate', 'analyses-list');
      }
    });
  }

  /**
   * Convert view name to router path
   * @param {string} view - View name
   * @param {Object} params - Navigation parameters
   * @returns {string} Router path
   */
  viewToRoutePath(view, params = {}) {
    // Map view names to router paths
    const viewToPath = {
      'dashboard': '',
      'analyses-list': 'analyses',
      'analysis-create': 'analyses/create',
      'analysis-view': params.id ? `analyses/${params.id}/view` : 'analyses',
      'analysis-edit': params.id ? `analyses/${params.id}/edit` : 'analyses',
      'analysis-types-list': 'analysis-types',
      'analysis-type-view': params.id ? `analysis-types/${params.id}/view` : 'analysis-types',
      'analysis-type-create': 'analysis-types/create',
      'analysis-type-edit': params.id ? `analysis-types/${params.id}/edit` : 'analysis-types',
      'source-types-list': 'source-types',
      'source-type-view': params.id ? `source-types/${params.id}/view` : 'source-types',
      'source-type-create': 'source-types/create',
      'source-type-edit': params.id ? `source-types/${params.id}/edit` : 'source-types',
      'organization-profiles': 'organization-profiles',
      'manage-types': 'manage-types',
      'new-analysis': 'new-analysis',
      'new-type': 'new-type',
      'new-type-ai': 'new-type-ai',
    };

    return viewToPath[view] || view;
  }

  filterNavigationParams(routePath, params = {}) {
    if (!params || Object.keys(params).length === 0) {
      return {};
    }

    const filtered = {};
    const match = this.router.match(routePath);
    const consumedKeys = match && match.params ? Object.keys(match.params) : [];

    Object.entries(params).forEach(([key, value]) => {
      if (value === undefined || value === null || value === '') {
        return;
      }

      if (consumedKeys.includes(key)) {
        return;
      }

      filtered[key] = value;
    });

    return filtered;
  }

  /**
   * Render page with top navigation
   * @param {HTMLElement} container - App container
   * @param {string} content - Page content HTML
   */
  renderWithNav(container, content) {
    const host = this.mainHost || document.getElementById('app-main');
    if (!host) {
      console.error('Main content host not found');
      return null;
    }

    host.innerHTML = `
      <div class="app-content">
        ${content}
      </div>
    `;

    return host.querySelector('.app-content');
  }

  /**
   * Navigate to a view (called by router)
   * @param {string} view - View name
   * @param {Object} params - Navigation parameters
   */
  async navigate(view, params = {}) {
    console.log(`[Meridian] Navigating to: ${view}`, params);

    this.state.currentView = view;

    const appContainer = this.appRoot || document.getElementById('app');
    if (!appContainer) {
      console.error('App container not found');
      return;
    }

    try {
      switch (view) {
        case 'dashboard':
          await this.renderDashboard(appContainer);
          break;

        case 'analyses-list':
          await this.renderAnalysesList(appContainer);
          break;

        case 'analysis-create':
          await this.renderAnalysisCreate(appContainer);
          break;

        case 'analysis-view':
          if (params.id) {
            await this.renderAnalysisView(appContainer, params.id);
          } else {
            this.toast.error('No analysis ID provided');
          }
          break;

        case 'analysis-edit':
          if (params.id) {
            await this.renderAnalysisEdit(appContainer, params.id);
          } else {
            this.toast.error('No analysis ID provided');
          }
          break;

        case 'new-analysis':
          // Redirect to new analysis create route
          await this.navigate('analysis-create', params);
          break;

        case 'new-type':
          await this.createType();
          break;

        case 'new-type-ai':
          await this.createTypeWithAI();
          break;

        case 'organization-profiles':
          await this.renderOrganizationProfiles(appContainer, params);
          break;

        case 'manage-types':
          await this.renderTypesManagement(appContainer);
          break;

        // Analysis Types Management
        case 'analysis-types-list':
          await this.renderAnalysisTypesList(appContainer);
          break;

        case 'analysis-type-view':
          if (params.id) {
            await this.renderAnalysisTypeView(appContainer, params.id);
          } else {
            this.toast.error('No type ID provided');
          }
          break;

        case 'analysis-type-create':
          await this.renderAnalysisTypeForm(appContainer, 'create');
          break;

        case 'analysis-type-edit':
          if (params.id) {
            await this.renderAnalysisTypeForm(appContainer, 'edit', params.id);
          } else {
            this.toast.error('No type ID provided');
          }
          break;

        // Source Types Management
        case 'source-types-list':
          await this.renderSourceTypesList(appContainer);
          break;

        case 'source-type-view':
          if (params.id) {
            await this.renderSourceTypeView(appContainer, params.id);
          } else {
            this.toast.error('No type ID provided');
          }
          break;

        case 'source-type-create':
          await this.renderSourceTypeForm(appContainer, 'create');
          break;

        case 'source-type-edit':
          if (params.id) {
            await this.renderSourceTypeEdit(appContainer, params.id);
          } else {
            this.toast.error('No type ID provided');
          }
          break;

        default:
          console.warn(`Unknown view: ${view}`);
          await this.renderDashboard(appContainer);
      }

      // Update top nav active state after navigation
      this.topNav.updateActiveState();
      this.sidebar.setActiveByView(view);

      if (window.matchMedia('(max-width: 1024px)').matches) {
        this.sidebar.close();
      }
    } catch (error) {
      console.error(`Failed to navigate to ${view}:`, error);
      this.toast.error(`Failed to load view: ${view}`);
    }
  }

  /**
   * Render Dashboard view
   */
  async renderDashboard(container) {
    const html = await this.dashboard.render();
    const contentArea = this.renderWithNav(container, html);
    if (!contentArea) return;

    this.dashboard.attachEventHandlers(contentArea);
  }

  /**
   * Render Analyses List view
   */
  async renderAnalysesList(container) {
    try {
      const pipelines = await this.api.getPipelines();
      this.state.pipelines = pipelines || [];

  this.updateSidebarPipelineBadges(this.state.pipelines);

      // Create PageHeader instance
      const pageHeader = new PageHeader(this.router, this.eventBus);

      const html = `
        <div class="analyses-list-view">
          ${pageHeader.render({
            title: 'All Analyses',
            subtitle: 'Browse and manage all your analysis pipelines',
            breadcrumbs: [
              {
                label: 'Home',
                path: '#/',
                icon: '<rect x="3" y="3" width="7" height="7"></rect><rect x="14" y="3" width="7" height="7"></rect><rect x="14" y="14" width="7" height="7"></rect><rect x="3" y="14" width="7" height="7"></rect>'
              },
              {
                label: 'Analyses',
                path: '#/analyses-list'
              }
            ],
            actions: [
              {
                label: 'New Analysis',
                action: 'new-analysis',
                variant: 'primary',
                icon: '<line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line>'
              }
            ]
          })}
          <div class="analyses-grid">
            ${this.renderAnalysesGrid(pipelines)}
          </div>
        </div>
      `;

      const contentArea = this.renderWithNav(container, html);
      if (!contentArea) {
        return;
      }

      // Attach PageHeader handlers
      pageHeader.attachEventHandlers(contentArea);

      // Listen for page header actions
      const headerActionHandler = (action) => {
        if (action === 'new-analysis') {
          this.eventBus.emit('navigate', 'new-analysis');
        }
      };
      this.eventBus.on('page-header-action', headerActionHandler);

      // No action buttons or event handlers on analysis cards - cards are now fully clickable links to detail view

    } catch (error) {
      console.error('Failed to render analyses list:', error);
      this.renderWithNav(container, `<div class="error-state"><h2>Failed to load analyses</h2><p>${error.message}</p></div>`);
    }
  }

  /**
   * Render analyses grid
   */
  renderAnalysesGrid(pipelines) {
    if (pipelines.length === 0) {
      return `
        <div class="empty-state">
          <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
            <polyline points="14 2 14 8 20 8"></polyline>
          </svg>
          <p>No analyses yet</p>
          <button class="btn btn-primary btn-press" data-action="new-analysis">Create Your First Analysis</button>
        </div>
      `;
    }

    return pipelines.map(pipeline => {
      const name = pipeline.name || pipeline.Name || 'Untitled Analysis';
      const id = pipeline.id || pipeline.Id;
      const typeName = pipeline.analysisType?.name || 'Unknown Type';
      const docCount = pipeline.documentCount || 0;
      const statusValue = pipeline.status || pipeline.Status || 'Unknown';
      const statusClass = statusValue.toString().toLowerCase().replace(/[^a-z0-9]+/g, '-');
      const updatedAt = pipeline.updatedAt || pipeline.UpdatedAt || pipeline.lastUpdated || pipeline.LastUpdated;
      const updatedLabel = this.formatRelativeDate(updatedAt);

      return `
        <a href="#/analyses/${id}/view" class="analysis-card-link">
          <div class="analysis-card" data-pipeline-id="${id}">
            <div class="analysis-card-header">
              <h3 class="analysis-card-title">${this.escapeHtml(name)}</h3>
              <span class="badge badge-${statusClass}">${this.escapeHtml(statusValue)}</span>
            </div>
            <p class="analysis-card-type">${this.escapeHtml(typeName)}</p>
            <div class="analysis-card-stats">
              <span>${docCount} ${docCount === 1 ? 'document' : 'documents'}</span>
            </div>
            <div class="analysis-card-meta">
              <span>${updatedLabel}</span>
            </div>
          </div>
        </a>
      `;
    }).join('');
  }

  /**
   * Render Types Management view
   */
  async renderTypesManagement(container) {
    try {
      const types = await this.api.getAnalysisTypes();
      this.state.analysisTypes = types || [];

      const html = `
        <div class="types-management-view">
          <div class="view-header">
            <h1 class="view-title">Analysis Types</h1>
            <div class="view-actions">
              <button class="btn btn-secondary btn-press" data-action="new-type">
                <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="12" y1="5" x2="12" y2="19"></line>
                  <line x1="5" y1="12" x2="19" y2="12"></line>
                </svg>
                Create Type
              </button>
              <button class="btn btn-primary btn-press" data-action="new-type-ai">
                <svg class="icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="3"></circle>
                  <path d="M12 1v6m0 6v6"></path>
                </svg>
                AI Create
              </button>
            </div>
          </div>
          <div class="types-grid">
            ${this.renderTypesGrid(types)}
          </div>
        </div>
      `;

      const contentArea = this.renderWithNav(container, html);
      if (!contentArea) {
        return;
      }

      contentArea.querySelector('[data-action="new-type"]')?.addEventListener('click', () => {
        this.navigate('new-type');
      });

      contentArea.querySelector('[data-action="new-type-ai"]')?.addEventListener('click', () => {
        this.navigate('new-type-ai');
      });

    } catch (error) {
      console.error('Failed to render types management:', error);
      const errorHtml = `<div class="error-state"><h2>Failed to load types</h2><p>${error.message}</p></div>`;
      this.renderWithNav(container, errorHtml);
    }
  }

  /**
   * Render types grid
   */
  renderTypesGrid(types) {
    if (types.length === 0) {
      return `
        <div class="empty-state">
          <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="12" y1="8" x2="12" y2="16"></line>
            <line x1="8" y1="12" x2="16" y2="12"></line>
          </svg>
          <p>No analysis types yet</p>
          <button class="btn btn-primary btn-press" data-action="new-type-ai">Create with AI</button>
        </div>
      `;
    }

    return types.map(type => {
      const name = type.name || type.Name || 'Untitled Type';
      const description = type.description || type.Description || 'No description';
      const id = type.id || type.Id;

      return `
        <div class="type-card" data-type-id="${id}">
          <div class="type-card-header">
            <h3 class="type-card-title">${this.escapeHtml(name)}</h3>
            <span class="type-badge">TYPE</span>
          </div>
          <p class="type-card-description">${this.escapeHtml(description)}</p>
          <button class="btn btn-secondary btn-sm" data-action="use-type" data-type-id="${id}">
            Use This Type
          </button>
        </div>
      `;
    }).join('');
  }

  /**
   * Open pipeline and render workspace
   */
  /**
   * Create analysis
   */
  async createAnalysis(typeId = null) {
    const name = prompt('Analysis name:', 'New Analysis');
    if (!name) return;

    try {
      const pipeline = await this.api.createPipeline({
        name,
        description: 'Created via Meridian UI',
        analysisTypeId: typeId
      });

      this.toast.success(`Analysis "${name}" created`);

      const pipelineId = pipeline.id || pipeline.Id;
  this.eventBus.emit('navigate', 'analysis-view', { id: pipelineId });
    } catch (error) {
      console.error('Failed to create analysis:', error);
      this.toast.error('Failed to create analysis');
    }
  }

  /**
   * Create type
   */
  async createType() {
    const name = prompt('Type name:', 'New Type');
    if (!name) return;

    const description = prompt('Type description:');

    try {
      await this.api.createAnalysisType({
        name,
        description
      });

      this.toast.success(`Type "${name}" created`);
      await this.navigate('manage-types');
    } catch (error) {
      console.error('Failed to create type:', error);
      this.toast.error('Failed to create type');
    }
  }

  /**
   * Create type with AI
   */
  async createTypeWithAI() {
    const promptText = prompt('Describe the analysis type you want (e.g., "An Enterprise Architecture Review with ServiceNow ID, architect, recommendation status")');
    if (!promptText) return;

    this.toast.info('AI is generating and saving type...');

    try {
      const createdType = await this.api.createAnalysisTypeWithAI(promptText);
      this.toast.success(`Created: ${createdType.name}`);
      // Redirect to edit the newly created type
      await this.navigate(`analysis-types/${createdType.id}/edit`);
    } catch (error) {
      console.error('Failed to create type with AI:', error);
      this.toast.error('Failed to create type with AI');
    }
  }

  /**
   * Render Analysis Create
   */
  async renderAnalysisCreate(container) {
    try {
      await this.analysisDetailView.initCreate();
      const html = this.analysisDetailView.render();
      const contentArea = this.renderWithNav(container, html);
      if (!contentArea) return;
      this.analysisDetailView.attachEventHandlers(contentArea);
    } catch (err) {
      const html = `<div class="error-state"><h2>Failed to initialize create form</h2><p>${this.escapeHtml(err.message)}</p></div>`;
      this.renderWithNav(container, html);
    }
  }

  /**
   * Render Analysis View
   */
  async renderAnalysisView(container, id) {
    try {
      const contentArea = this.renderWithNav(container, '<div class="content-area">' + this.analysisDetailView.renderSkeleton() + '</div>');
      if (!contentArea) return;
      await this.analysisDetailView.load(id);
      this.analysisDetailView.isEditing = false;
      this.analysisDetailView.isCreating = false;
      const html = this.analysisDetailView.render();
      contentArea.innerHTML = html;
      this.analysisDetailView.attachEventHandlers(contentArea.closest('.app-content'));
    } catch (err) {
      const html = `<div class="error-state"><h2>Failed to load analysis</h2><p>${this.escapeHtml(err.message)}</p></div>`;
      this.renderWithNav(container, html);
    }
  }

  /**
   * Render Analysis Edit
   */
  async renderAnalysisEdit(container, id) {
    try {
      const contentArea = this.renderWithNav(container, '<div class="content-area">' + this.analysisDetailView.renderSkeleton() + '</div>');
      if (!contentArea) return;
      await this.analysisDetailView.load(id);
      this.analysisDetailView.isEditing = true;
      this.analysisDetailView.isCreating = false;
      const html = this.analysisDetailView.render();
      contentArea.innerHTML = html;
      this.analysisDetailView.attachEventHandlers(contentArea.closest('.app-content'));
    } catch (err) {
      const html = `<div class="error-state"><h2>Failed to load analysis</h2><p>${this.escapeHtml(err.message)}</p></div>`;
      this.renderWithNav(container, html);
    }
  }

  /**
   * Render Analysis Types List
   */
  async renderAnalysisTypesList(container) {
    const html = await this.analysisTypesManager.render();
    const contentArea = this.renderWithNav(container, html);
    if (!contentArea) return;

    this.analysisTypesManager.attachEventHandlers(contentArea);
  }

  /**
   * Render Analysis Type View
   */
  async renderAnalysisTypeView(container, id) {
    try {
      const contentArea = this.renderWithNav(container, '<div class="content-area">' + this.analysisTypeDetailView.renderSkeleton() + '</div>');
      if (!contentArea) return;
      await this.analysisTypeDetailView.load(id);
      this.analysisTypeDetailView.isEditing = false;
      this.analysisTypeDetailView.refresh(contentArea.closest('.app-content'));
    } catch (err) {
      const html = `<div class="error-state"><h2>Failed to load analysis type</h2><p>${this.escapeHtml(err.message)}</p></div>`;
      this.renderWithNav(container, html);
    }
  }

  /**
   * Render Analysis Type Form (Create/Edit)
   */
  async renderAnalysisTypeForm(container, mode, id = null) {
    const typeFormView = new TypeFormView(mode, 'analysis', this.api, this.eventBus, this.toast);
    const html = await typeFormView.render(id);
    const contentArea = this.renderWithNav(container, html);
    if (!contentArea) return;

    typeFormView.attachEventHandlers(contentArea);
  }

  async renderOrganizationProfiles(container, params = {}) {
    const html = await this.organizationProfilesManager.render(params);
    const contentArea = this.renderWithNav(container, html);
    if (!contentArea) return;

    this.organizationProfilesManager.attachEventHandlers(contentArea);
  }

  /**
   * Render Source Types List
   */
  async renderSourceTypesList(container) {
    const html = await this.sourceTypesManager.render();
    const contentArea = this.renderWithNav(container, html);
    if (!contentArea) return;

    this.sourceTypesManager.attachEventHandlers(contentArea);
  }

  /**
   * Render Source Type View
   */
  async renderSourceTypeView(container, id) {
    try {
      const contentArea = this.renderWithNav(container, '<div class="content-area">' + this.sourceTypeDetailView.renderSkeleton() + '</div>');
      if (!contentArea) return;
      await this.sourceTypeDetailView.load(id);
      this.sourceTypeDetailView.isEditing = false;
      this.sourceTypeDetailView.refresh(contentArea.closest('.app-content'));
    } catch (err) {
      const html = `<div class="error-state"><h2>Failed to load source type</h2><p>${this.escapeHtml(err.message)}</p></div>`;
      this.renderWithNav(container, html);
    }
  }

  /**
   * Render Source Type Form (Create/Edit)
   */
  async renderSourceTypeForm(container, mode, id = null) {
    const typeFormView = new TypeFormView(mode, 'source', this.api, this.eventBus, this.toast);
    const html = await typeFormView.render(id);
    const contentArea = this.renderWithNav(container, html);
    if (!contentArea) return;

    typeFormView.attachEventHandlers(contentArea);
  }

  /**
   * Render Source Type Edit (Full-page detail view in edit mode)
   */
  async renderSourceTypeEdit(container, id) {
    try {
      const contentArea = this.renderWithNav(container, '<div class="content-area">' + this.sourceTypeDetailView.renderSkeleton() + '</div>');
      if (!contentArea) return;
      await this.sourceTypeDetailView.load(id);
      this.sourceTypeDetailView.isEditing = true;
      this.sourceTypeDetailView.refresh(contentArea.closest('.app-content'));
    } catch (err) {
      const html = `<div class="error-state"><h2>Failed to load source type</h2><p>${this.escapeHtml(err.message)}</p></div>`;
      this.renderWithNav(container, html);
    }
  }

  updateSidebarPipelineBadges(pipelines) {
    if (!Array.isArray(pipelines)) {
      return;
    }

    const total = pipelines.length;
    const favorites = pipelines.filter(pipeline => {
      const id = (pipeline.id || pipeline.Id || '').toString();
      return this.dashboard.isFavorite('analysis', id);
    }).length;

    const active = pipelines.filter(pipeline => {
      const status = (pipeline.status || pipeline.Status || '').toString().toLowerCase();
      return status && status !== 'completed' && status !== 'archived';
    }).length;

    const badgeMap = {
      'all-analyses': total,
      favorites,
      recent: Math.min(total, 5),
      'active-analyses': active
    };

    this.sidebar.updateBadges(badgeMap);
  }

  async openPipelineDetail(pipelineId) {
    if (!pipelineId) {
      return;
    }

    try {
      const id = pipelineId.toString();
      let pipeline = this.state.pipelines.find(p => (p.id || p.Id || '').toString() === id);

      if (!pipeline) {
        pipeline = await this.api.getPipeline(id);
      }

      if (!pipeline) {
        throw new Error('Pipeline not found');
      }

      const normalized = {
        id,
        name: pipeline.name || pipeline.Name || 'Untitled Analysis',
        description: pipeline.description || pipeline.Description || '',
        tags: pipeline.tags || pipeline.Tags || [],
        status: pipeline.status || pipeline.Status || 'Unknown',
        documentCount: pipeline.documentCount || pipeline.DocumentCount || 0,
        updatedAt: pipeline.updatedAt || pipeline.UpdatedAt || pipeline.lastUpdated || pipeline.LastUpdated,
      };

      const meta = {
        Status: normalized.status,
        Documents: `${normalized.documentCount}`,
        Updated: this.formatRelativeDate(normalized.updatedAt)
      };

      this.detailPanel.onSave = async (updates) => {
        const payload = { ...pipeline, ...updates };
        await this.api.pipelines.update(id, payload);
        Object.assign(pipeline, payload);
        this.toast.success('Analysis updated');
        this.updateSidebarPipelineBadges(this.state.pipelines);
      };

      this.detailPanel.onDelete = async (deleteId) => {
        await this.api.deletePipeline(deleteId);
        this.toast.success('Analysis deleted');
        this.state.pipelines = this.state.pipelines.filter(p => (p.id || p.Id || '').toString() !== deleteId);
        this.updateSidebarPipelineBadges(this.state.pipelines);
        if (this.state.currentView === 'analyses-list') {
          await this.renderAnalysesList(this.appRoot || document.getElementById('app'));
        }
      };

      this.detailPanel.open({
        ...normalized,
        description: normalized.description,
        tags: normalized.tags,
        meta,
        links: {
          workspace: `#/analyses/${id}/view`
        }
      }, 'view');
    } catch (error) {
      console.error('Failed to open pipeline detail panel:', error);
      this.toast.error('Unable to open analysis details');
    }
  }

  formatRelativeDate(value) {
    if (!value) {
      return 'Updated recently';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return 'Updated recently';
    }

    const diffMs = Date.now() - date.getTime();
    if (diffMs < 0) {
      return 'Updated just now';
    }

    const minutes = Math.floor(diffMs / (1000 * 60));
    if (minutes < 1) {
      return 'Updated just now';
    }
    if (minutes < 60) {
      return `Updated ${minutes} min${minutes === 1 ? '' : 's'} ago`;
    }

    const hours = Math.floor(minutes / 60);
    if (hours < 24) {
      return `Updated ${hours} hr${hours === 1 ? '' : 's'} ago`;
    }

    const days = Math.floor(hours / 24);
    if (days < 7) {
      return `Updated ${days} day${days === 1 ? '' : 's'} ago`;
    }

    const weeks = Math.floor(days / 7);
    if (weeks < 5) {
      return `Updated ${weeks} week${weeks === 1 ? '' : 's'} ago`;
    }

    const months = Math.floor(days / 30);
    if (months < 12) {
      return `Updated ${months} month${months === 1 ? '' : 's'} ago`;
    }

    const years = Math.floor(days / 365);
    return `Updated ${years} year${years === 1 ? '' : 's'} ago`;
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
   * Format file size in human-readable format
   */
  formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
  }
}

// Initialize app when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    window.app = new MeridianApp();
    window.app.init();
  });
} else {
  window.app = new MeridianApp();
  window.app.init();
}
