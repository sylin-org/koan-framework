import { apiClient } from './client';
import type { Job, JobListResponse, ActiveJobsResponse, AllJobsResponse, ListJobsRequest } from './types';

/**
 * Jobs API - Query indexing job status
 */
export const jobsApi = {
  /**
   * Get a specific indexing job by ID
   */
  async get(jobId: string): Promise<Job> {
    const { data } = await apiClient.get<Job>(`/jobs/${jobId}`);
    return data;
  },

  /**
   * List all indexing jobs for a specific project
   */
  async listByProject(projectId: string, limit = 10): Promise<JobListResponse> {
    const { data } = await apiClient.get<JobListResponse>(`/jobs/project/${projectId}`, {
      params: { limit },
    });
    return data;
  },

  /**
   * Get the current/latest indexing job for a project
   */
  async getCurrent(projectId: string): Promise<Job & { isActive: boolean }> {
    const { data } = await apiClient.get<Job & { isActive: boolean }>(`/jobs/project/${projectId}/current`);
    return data;
  },

  /**
   * List all indexing jobs with filtering and pagination
   */
  async listAll(request: ListJobsRequest = {}): Promise<AllJobsResponse> {
    const { data } = await apiClient.get<AllJobsResponse>('/jobs', {
      params: {
        projectId: request.projectId,
        status: request.status,
        limit: request.limit || 50,
        offset: request.offset || 0,
      },
    });
    return data;
  },

  /**
   * List all active (in-progress) indexing jobs across all projects
   */
  async listActive(): Promise<ActiveJobsResponse> {
    const { data } = await apiClient.get<ActiveJobsResponse>('/jobs/active');
    return data;
  },

  /**
   * Cancel an in-progress indexing job
   */
  async cancel(jobId: string): Promise<{ id: string; status: string; message: string }> {
    const { data } = await apiClient.post(`/jobs/${jobId}/cancel`);
    return data;
  },
};
