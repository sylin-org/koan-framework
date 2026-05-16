import { apiClient } from './client';
import type {
  Project,
  CreateProjectRequest,
  IndexMetadataRequest,
  UpdateMonitoringRequest,
  BulkIndexRequest,
  ProjectStatusResponse,
  ProjectHealthResponse,
} from './types';

/**
 * Projects API - Manage code projects
 */
export const projectsApi = {
  /**
   * List all projects with optional filtering
   */
  async list(): Promise<Project[]> {
    const { data } = await apiClient.get<Project[]>('/projects');
    return data;
  },

  /**
   * Get a single project by ID
   */
  async get(id: string): Promise<Project> {
    const { data } = await apiClient.get<Project>(`/projects/${id}`);
    return data;
  },

  /**
   * Create a new project
   */
  async create(request: CreateProjectRequest): Promise<Project> {
    const { data } = await apiClient.post<Project>('/projects/create', request);
    return data;
  },

  /**
   * Update an existing project
   */
  async update(id: string, project: Partial<Project>): Promise<Project> {
    const { data } = await apiClient.put<Project>(`/projects/${id}`, project);
    return data;
  },

  /**
   * Delete a project
   */
  async delete(id: string): Promise<void> {
    await apiClient.delete(`/projects/${id}`);
  },

  /**
   * Mark project as indexed
   */
  async markIndexed(id: string, metadata: IndexMetadataRequest): Promise<Project> {
    const { data } = await apiClient.post<Project>(`/projects/${id}/indexed`, metadata);
    return data;
  },

  /**
   * Get active projects only
   */
  async getActive(): Promise<Project[]> {
    const { data } = await apiClient.get<Project[]>('/projects/active');
    return data;
  },

  /**
   * Trigger indexing for a project
   */
  async index(id: string, force = false): Promise<{ message: string; projectId: string; statusUrl: string }> {
    const { data } = await apiClient.post(`/projects/${id}/index`, null, {
      params: { force },
    });
    return data;
  },

  /**
   * Get project indexing status
   */
  async getStatus(id: string): Promise<ProjectStatusResponse> {
    const { data } = await apiClient.get<ProjectStatusResponse>(`/projects/${id}/status`);
    return data;
  },

  /**
   * Get project health status
   */
  async getHealth(id: string): Promise<ProjectHealthResponse> {
    const { data} = await apiClient.get<ProjectHealthResponse>(`/projects/${id}/health`);
    return data;
  },

  /**
   * Bulk index multiple projects
   */
  async bulkIndex(request: BulkIndexRequest): Promise<{
    message: string;
    total: number;
    started: number;
    notFound: number;
    projects: Array<{ projectId: string; name: string; message: string; statusUrl: string }>;
    projectsNotFound: string[];
  }> {
    const { data } = await apiClient.post('/projects/bulk-index', request);
    return data;
  },

  /**
   * Update project monitoring settings
   */
  async updateMonitoring(id: string, settings: UpdateMonitoringRequest): Promise<{ message: string; project: Project }> {
    const { data } = await apiClient.patch(`/projects/${id}/monitoring`, settings);
    return data;
  },

  /**
   * Manually trigger project re-indexing
   */
  async reindex(id: string, force = false): Promise<{ message: string; projectId: string; statusUrl: string }> {
    const { data } = await apiClient.post(`/projects/${id}/reindex`, null, {
      params: { force },
    });
    return data;
  },
};
