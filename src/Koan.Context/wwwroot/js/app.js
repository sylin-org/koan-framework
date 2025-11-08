/**
 * Main Application
 * Coordinates the dashboard UI and real-time updates
 */
import { ApiClient } from './api.js';
import { MetricCard } from './components/MetricCard.js';
import { JobProgressCard } from './components/JobProgressCard.js';
import { ProjectCard } from './components/ProjectCard.js';
import { SSEClient } from './components/SSEClient.js';
import { LoadingSpinner } from './components/LoadingSpinner.js';
import { CardGrid } from './components/CardGrid.js';
import { Alert } from './components/Alert.js';
import { EmptyState } from './components/EmptyState.js';
import { PerformanceChart } from './components/PerformanceChart.js';

export class DashboardApp {
  constructor() {
    this.api = new ApiClient();
    this.sseClient = null;
    this.activeJobs = new Map();
  }

  async init() {
    console.log('[Dashboard] Initializing...');

    // Load initial data
    await this.loadDashboard();

    // Setup event listeners
    this.setupEventListeners();

    // Connect to SSE for real-time updates
    this.connectSSE();

    // Refresh data periodically
    setInterval(() => this.refreshData(), 30000); // Every 30 seconds
  }

  async loadDashboard() {
    try {
      // Show loading state
      LoadingSpinner.show('#metrics-container', { message: 'Loading metrics...' });
      LoadingSpinner.show('#projects-container', { message: 'Loading projects...' });
      LoadingSpinner.show('#jobs-container', { message: 'Loading jobs...' });

      // Load metrics
      await this.loadMetrics();

      // Load projects
      await this.loadProjects();

      // Load active jobs
      await this.loadJobs();

    } catch (error) {
      console.error('[Dashboard] Error loading:', error);
      Alert.show('#alerts-container', {
        variant: 'danger',
        title: 'Error',
        message: 'Failed to load dashboard data: ' + error.message
      });
    }
  }

  async loadMetrics() {
    try {
      const response = await this.api.getMetricsSummary();
      const metrics = response.data;

      const metricCards = [
        new MetricCard({
          title: 'Total Projects',
          value: metrics.projects.total,
          change: `+${metrics.projects.changeToday} today`,
          trend: metrics.projects.changeToday > 0 ? 'up' : 'stable',
          icon: '📁'
        }),
        new MetricCard({
          title: 'Ready',
          value: metrics.projects.ready,
          variant: 'success',
          icon: '✅'
        }),
        new MetricCard({
          title: 'Indexing',
          value: metrics.projects.indexing,
          variant: 'primary',
          icon: '⚡'
        }),
        new MetricCard({
          title: 'Failed',
          value: metrics.projects.failed,
          variant: metrics.projects.failed > 0 ? 'danger' : 'default',
          icon: '❌'
        }),
        new MetricCard({
          title: 'Total Chunks',
          value: metrics.chunks.total.toLocaleString(),
          change: `+${metrics.chunks.changeToday} today`,
          trend: metrics.chunks.changeTrend,
          icon: '📄'
        }),
        new MetricCard({
          title: 'Avg Latency',
          value: metrics.performance.avgLatencyMs + ' ms',
          icon: '⏱️'
        })
      ];

      const grid = new CardGrid({
        items: metricCards,
        columns: 'auto',
        gap: '4'
      });

      document.getElementById('metrics-container').innerHTML = grid.render();

    } catch (error) {
      console.error('[Dashboard] Error loading metrics:', error);
      throw error;
    }
  }

  async loadProjects() {
    try {
      const projects = await this.api.getProjects();

      if (!projects || projects.length === 0) {
        EmptyState.renderTo('#projects-container', {
          title: 'No Projects Yet',
          message: 'Get started by creating your first project.',
          icon: '📂',
          actionText: 'Create Project',
          onAction: () => this.showCreateProjectDialog()
        });
        return;
      }

      const projectCards = projects.map(project =>
        new ProjectCard({
          project,
          onIndex: (id) => this.indexProject(id),
          onDelete: (id) => this.deleteProject(id),
          onView: (id) => this.viewProject(id)
        })
      );

      const grid = new CardGrid({
        items: projectCards,
        columns: 'auto',
        gap: '4'
      });

      document.getElementById('projects-container').innerHTML = grid.render();

      // Attach event listeners
      this.attachProjectEventListeners();

    } catch (error) {
      console.error('[Dashboard] Error loading projects:', error);
      throw error;
    }
  }

  async loadJobs() {
    try {
      // This would call an actual jobs endpoint
      // For now, show empty state
      EmptyState.renderTo('#jobs-container', {
        ...EmptyState.noJobs(),
        icon: '⏸️'
      });

    } catch (error) {
      console.error('[Dashboard] Error loading jobs:', error);
      throw error;
    }
  }

  connectSSE() {
    this.sseClient = new SSEClient({
      url: '/api/stream/jobs',
      onMessage: (data, event, eventType) => this.handleSSEMessage(data, eventType),
      onError: (error) => console.error('[SSE] Error:', error),
      onOpen: () => console.log('[SSE] Connected'),
      onClose: (reason) => console.log('[SSE] Disconnected:', reason),
      reconnectDelay: 3000,
      maxReconnectAttempts: 5
    });

    this.sseClient.connect();
  }

  handleSSEMessage(data, eventType) {
    console.log(`[SSE] ${eventType}:`, data);

    switch (eventType) {
      case 'job-update':
        this.updateJob(data);
        break;
      case 'job-removed':
        this.removeJob(data.jobId);
        break;
      case 'heartbeat':
        // Just log heartbeats
        break;
      default:
        console.log('[SSE] Unknown event type:', eventType);
    }
  }

  updateJob(jobData) {
    this.activeJobs.set(jobData.jobId, jobData);
    this.renderJobs();
  }

  removeJob(jobId) {
    this.activeJobs.delete(jobId);
    this.renderJobs();
  }

  renderJobs() {
    const container = document.getElementById('jobs-container');
    if (!container) return;

    if (this.activeJobs.size === 0) {
      EmptyState.renderTo(container, EmptyState.noJobs());
      return;
    }

    const jobCards = Array.from(this.activeJobs.values()).map(job =>
      new JobProgressCard({
        job,
        onCancel: (id) => this.cancelJob(id)
      })
    );

    const grid = new CardGrid({
      items: jobCards,
      columns: 'auto',
      gap: '4'
    });

    container.innerHTML = grid.render();
  }

  attachProjectEventListeners() {
    // Index buttons
    document.querySelectorAll('.project-index-btn').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        const projectId = e.target.dataset.projectId;
        await this.indexProject(projectId);
      });
    });

    // Delete buttons
    document.querySelectorAll('.project-delete-btn').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        const projectId = e.target.dataset.projectId;
        if (confirm('Are you sure you want to delete this project?')) {
          await this.deleteProject(projectId);
        }
      });
    });
  }

  async indexProject(projectId) {
    try {
      await this.api.indexProject(projectId);
      Alert.show('#alerts-container', {
        variant: 'success',
        message: 'Indexing started successfully'
      });
      await this.refreshData();
    } catch (error) {
      Alert.show('#alerts-container', {
        variant: 'danger',
        title: 'Error',
        message: 'Failed to start indexing: ' + error.message
      });
    }
  }

  async deleteProject(projectId) {
    try {
      await this.api.deleteProject(projectId);
      Alert.show('#alerts-container', {
        variant: 'success',
        message: 'Project deleted successfully'
      });
      await this.refreshData();
    } catch (error) {
      Alert.show('#alerts-container', {
        variant: 'danger',
        title: 'Error',
        message: 'Failed to delete project: ' + error.message
      });
    }
  }

  setupEventListeners() {
    // Refresh button
    const refreshBtn = document.getElementById('refresh-btn');
    if (refreshBtn) {
      refreshBtn.addEventListener('click', () => this.refreshData());
    }
  }

  async refreshData() {
    console.log('[Dashboard] Refreshing data...');
    await this.loadProjects();
    await this.loadMetrics();
  }

  showCreateProjectDialog() {
    // In a real app, this would show a modal/dialog
    Alert.show('#alerts-container', {
      variant: 'info',
      message: 'Create project dialog would appear here'
    });
  }
}

// Initialize app when DOM is loaded
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    const app = new DashboardApp();
    app.init();
  });
} else {
  const app = new DashboardApp();
  app.init();
}
