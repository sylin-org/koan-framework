/**
 * API Client
 * Handles all HTTP requests to the Koan.Context API
 */
export class ApiClient {
  constructor(baseUrl = '') {
    this.baseUrl = baseUrl;
  }

  async get(endpoint) {
    const response = await fetch(`${this.baseUrl}${endpoint}`);
    if (!response.ok) {
      throw new Error(`API Error: ${response.status} ${response.statusText}`);
    }
    return await response.json();
  }

  async post(endpoint, data) {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(data)
    });
    if (!response.ok) {
      throw new Error(`API Error: ${response.status} ${response.statusText}`);
    }
    return await response.json();
  }

  async delete(endpoint) {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'DELETE'
    });
    if (!response.ok) {
      throw new Error(`API Error: ${response.status} ${response.statusText}`);
    }
    return response.status === 204 ? null : await response.json();
  }

  // Metrics endpoints
  async getMetricsSummary() {
    return await this.get('/api/metrics/summary');
  }

  async getPerformanceMetrics(period = '24h') {
    return await this.get(`/api/metrics/performance?period=${period}`);
  }

  async getHealth() {
    return await this.get('/api/metrics/health');
  }

  // Project endpoints
  async getProjects() {
    return await this.get('/api/projects');
  }

  async getProject(id) {
    return await this.get(`/api/projects/${id}`);
  }

  async createProject(name, rootPath, docsPath = null) {
    return await this.post('/api/projects/create', { name, rootPath, docsPath });
  }

  async indexProject(id, force = false) {
    return await this.post(`/api/projects/${id}/index?force=${force}`);
  }

  async deleteProject(id) {
    return await this.delete(`/api/projects/${id}`);
  }

  async getProjectStatus(id) {
    return await this.get(`/api/projects/${id}/status`);
  }

  // Job endpoints
  async getJobs() {
    return await this.get('/api/jobs');
  }

  async getJob(id) {
    return await this.get(`/api/jobs/${id}`);
  }
}
