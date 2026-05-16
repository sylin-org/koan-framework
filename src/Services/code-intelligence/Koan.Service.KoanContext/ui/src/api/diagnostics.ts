import { apiClient } from './client';
import type {
  TagDistributionResponse,
  TagDistributionEntry,
  ChunkSampleResponse,
} from './types';

/**
 * Diagnostics API - surfacing tag insights and sample data.
 */
export const diagnosticsApi = {
  /**
   * Fetch tag distribution across all indexed chunks.
   */
  async getTagDistribution(): Promise<TagDistributionResponse> {
    const { data } = await apiClient.get<TagDistributionResponse>('/diagnostics/tags');

    // Ensure tags are sorted descending to provide a stable experience.
    const sortedTags: TagDistributionEntry[] = [...data.tags].sort((a, b) => b.count - a.count);

    return {
      ...data,
      tags: sortedTags,
    };
  },

  /**
   * Sample chunks for lightweight inspection.
   */
  async getChunkSample(options?: { projectId?: string; count?: number }): Promise<ChunkSampleResponse> {
    const params = new URLSearchParams();

    if (options?.projectId) {
      params.set('projectId', options.projectId);
    }

    if (options?.count) {
      params.set('count', options.count.toString());
    }

    const query = params.toString();
    const url = `/diagnostics/chunks/sample${query.length > 0 ? `?${query}` : ''}`;
    const { data } = await apiClient.get<ChunkSampleResponse>(url);
    return data;
  },
};
