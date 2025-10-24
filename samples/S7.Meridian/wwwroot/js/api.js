import { EntityClient } from './services/EntityClient.js';

/**
 * Meridian API Client
 * Handles all communication with the Meridian backend API
 * Uses EntityClient for standard CRUD operations
 */
export class API {
  constructor(baseUrl = '') {
    this.baseUrl = baseUrl || window.location.origin;

    // Initialize entity clients for standard CRUD
    this.analysisTypes = new EntityClient('analysistypes', this.baseUrl);
    this.sourceTypes = new EntityClient('sourcetypes', this.baseUrl);
    this.pipelines = new EntityClient('pipelines', this.baseUrl);
  }

  /**
   * Make a generic API request
   * @param {string} path - API endpoint path
   * @param {Object} options - Fetch options
   * @returns {Promise<any>} Response data
   */
  async request(path, options = {}) {
    const url = `${this.baseUrl}${path}`;

    const defaultOptions = {
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
      },
      ...options,
    };

    try {
      const response = await fetch(url, defaultOptions);

      // Get trace ID from headers for debugging
      const traceId = response.headers.get('Koan-Trace-Id');
      if (traceId) {
        console.log(`[API] Trace-Id: ${traceId}`);
      }

      if (!response.ok) {
        const error = new Error(`HTTP ${response.status}: ${response.statusText}`);
        error.status = response.status;
        error.response = response;

        // Try to get error details from response
        try {
          const errorData = await response.json();
          error.data = errorData;
        } catch (e) {
          // Response wasn't JSON
        }

        throw error;
      }

      // Handle empty responses (204 No Content)
      if (response.status === 204) {
        return null;
      }

      // Handle text/plain responses
      const contentType = response.headers.get('Content-Type');
      if (contentType && contentType.includes('text/plain')) {
        return await response.text();
      }

      // Default: parse as JSON
      return await response.json();
    } catch (error) {
      console.error(`[API Error] ${options.method || 'GET'} ${path}:`, error);
      throw error;
    }
  }

  /**
   * GET request
   */
  async get(path, options = {}) {
    return this.request(path, { ...options, method: 'GET' });
  }

  /**
   * POST request
   */
  async post(path, body, options = {}) {
    return this.request(path, {
      ...options,
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  /**
   * PUT request
   */
  async put(path, body, options = {}) {
    return this.request(path, {
      ...options,
      method: 'PUT',
      body: JSON.stringify(body),
    });
  }

  /**
   * PATCH request
   */
  async patch(path, body, options = {}) {
    return this.request(path, {
      ...options,
      method: 'PATCH',
      body: JSON.stringify(body),
    });
  }

  /**
   * DELETE request
   */
  async delete(path, options = {}) {
    return this.request(path, { ...options, method: 'DELETE' });
  }

  // ==================== Analysis Types ====================

  /**
   * Get all analysis types
   */
  async getAnalysisTypes() {
    return this.analysisTypes.getAll();
  }

  /**
   * Get analysis type by ID
   */
  async getAnalysisType(id) {
    return this.analysisTypes.getById(id);
  }

  /**
   * Create analysis type
   */
  async createAnalysisType(analysisType) {
    return this.analysisTypes.create(analysisType);
  }

  /**
   * AI-generate analysis type from goal/audience
   */
  async suggestAnalysisType(prompt) {
    return this.post('/api/analysistypes/ai-suggest', {
      prompt
    });
  }

  /**
   * AI-create analysis type (generates AND saves)
   */
  async createAnalysisTypeWithAI(prompt) {
    return this.post('/api/analysistypes/ai-create', {
      prompt
    });
  }

  /**
   * Get analysis type template for new entity
   */
  async getAnalysisTypeTemplate() {
    return this.get('/api/analysistypes/new');
  }

  /**
   * Update analysis type (PATCH)
   * @param {string} id - Type ID
   * @param {Object} updates - Fields to update
   */
  async updateAnalysisType(id, updates) {
    // Convert to JSON Patch format
    const patches = Object.entries(updates).map(([key, value]) => ({
      op: 'replace',
      path: `/${key}`,
      value: value
    }));

    return this.request(`/api/analysistypes/${id}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json-patch+json' },
      body: JSON.stringify(patches)
    });
  }

  /**
   * Delete analysis type
   */
  async deleteAnalysisType(id) {
    return this.analysisTypes.delete(id);
  }

  /**
   * Bulk delete analysis types
   */
  async bulkDeleteAnalysisTypes(ids) {
    return this.request('/api/analysistypes/bulk', {
      method: 'DELETE',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(ids)
    });
  }

  // ==================== Source Types ====================

  /**
   * Get all source types
   */
  async getSourceTypes() {
    return this.sourceTypes.getAll();
  }

  /**
   * Get source type by ID
   */
  async getSourceType(id) {
    return this.sourceTypes.getById(id);
  }

  /**
   * Get source type template
   */
  async getSourceTypeTemplate() {
    return this.get('/api/sourcetypes/new');
  }

  /**
   * Create source type
   */
  async createSourceType(sourceType) {
    return this.sourceTypes.create(sourceType);
  }

  /**
   * Update source type (PATCH)
   * @param {string} id - Type ID
   * @param {Object} updates - Fields to update
   */
  async updateSourceType(id, updates) {
    const patches = Object.entries(updates).map(([key, value]) => ({
      op: 'replace',
      path: `/${key}`,
      value: value
    }));

    return this.request(`/api/sourcetypes/${id}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json-patch+json' },
      body: JSON.stringify(patches)
    });
  }

  /**
   * Delete source type
   */
  async deleteSourceType(id) {
    return this.sourceTypes.delete(id);
  }

  /**
   * AI-generate source type
   */
  async suggestSourceType(goal, audience, additionalContext = '') {
    return this.post('/api/sourcetypes/ai-suggest', {
      goal,
      audience,
      additionalContext
    });
  }

  /**
   * Bulk create source types
   */
  async bulkCreateSourceTypes(sourceTypes) {
    return this.post('/api/sourcetypes/bulk', sourceTypes);
  }

  /**
   * Bulk delete source types
   */
  async bulkDeleteSourceTypes(ids) {
    return this.request('/api/sourcetypes/bulk', {
      method: 'DELETE',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(ids)
    });
  }

  // ==================== Pipelines ====================

  /**
   * Get all pipelines
   */
  async getPipelines() {
    return this.pipelines.getAll();
  }

  /**
   * Get pipeline by ID
   */
  async getPipeline(id) {
    return this.pipelines.getById(id);
  }

  /**
   * Create pipeline
   */
  async createPipeline(pipeline) {
    return this.pipelines.create(pipeline);
  }

  /**
   * Delete pipeline
   */
  async deletePipeline(id) {
    return this.pipelines.delete(id);
  }

  // ==================== Authoritative Notes ====================

  /**
   * Get authoritative notes for a pipeline
   */
  async getNotes(pipelineId) {
    return this.get(`/api/pipelines/${pipelineId}/notes`);
  }

  /**
   * Set authoritative notes for a pipeline
   * @param {string} pipelineId - Pipeline ID
   * @param {string} notes - Notes content
   * @param {boolean} reProcess - Whether to re-process documents with new notes
   */
  async setNotes(pipelineId, notes, reProcess = false) {
    return this.put(`/api/pipelines/${pipelineId}/notes`, {
      authoritativeNotes: notes,
      reProcess,
    });
  }

  // ==================== Documents ====================

  /**
   * Upload document to pipeline
   * @param {string} pipelineId - Pipeline ID
   * @param {File} file - File to upload
   * @returns {Promise<Object>} Upload job response
   */
  async uploadDocument(pipelineId, file) {
    const formData = new FormData();
    formData.append('file', file);

    return this.request(`/api/pipelines/${pipelineId}/documents`, {
      method: 'POST',
      headers: {
        // Don't set Content-Type - let browser set it with boundary for multipart/form-data
      },
      body: formData,
    });
  }

  /**
   * Upload document content (text) to pipeline
   * @param {string} pipelineId - Pipeline ID
   * @param {string} fileName - File name
   * @param {string} content - Document content
   * @returns {Promise<Object>} Upload job response
   */
  async uploadDocumentContent(pipelineId, fileName, content) {
    return this.post(`/api/pipelines/${pipelineId}/documents/content`, {
      fileName,
      content,
    });
  }

  /**
   * Get documents for a pipeline
   */
  async getDocuments(pipelineId) {
    return this.get(`/api/pipelines/${pipelineId}/documents`);
  }

  // ==================== Jobs ====================

  /**
   * Get job status
   */
  async getJob(pipelineId, jobId) {
    return this.get(`/api/pipelines/${pipelineId}/jobs/${jobId}`);
  }

  /**
   * Poll job until completed
   * @param {string} pipelineId - Pipeline ID
   * @param {string} jobId - Job ID
   * @param {Function} onProgress - Progress callback
   * @returns {Promise<Object>} Completed job
   */
  async waitForJob(pipelineId, jobId, onProgress = null) {
    const pollInterval = 1000; // 1 second
    const maxAttempts = 300; // 5 minutes max
    let attempts = 0;

    while (attempts < maxAttempts) {
      const job = await this.getJob(pipelineId, jobId);

      if (onProgress) {
        onProgress(job);
      }

      const status = job.status || job.Status;

      if (status === 'Completed' || status === 'completed') {
        return job;
      }

      if (status === 'Failed' || status === 'failed') {
        throw new Error(`Job ${jobId} failed`);
      }

      await new Promise(resolve => setTimeout(resolve, pollInterval));
      attempts++;
    }

    throw new Error(`Job ${jobId} timed out after ${maxAttempts} attempts`);
  }

  // ==================== Deliverables ====================

  /**
   * Get latest deliverable for a pipeline
   */
  async getDeliverable(pipelineId) {
    return this.get(`/api/pipelines/${pipelineId}/deliverables/latest`);
  }

  /**
   * Get deliverable markdown
   */
  async getDeliverableMarkdown(pipelineId) {
    return this.get(`/api/pipelines/${pipelineId}/deliverables/markdown`);
  }

  /**
   * Get deliverable JSON
   */
  async getDeliverableJson(pipelineId) {
    return this.get(`/api/pipelines/${pipelineId}/deliverables/json`);
  }
}
