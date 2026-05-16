import { apiClient } from './client';
import type { TagPipelineDto, TagPipelineRequestDto } from './types';

/**
 * Tag pipeline management endpoints.
 */
export const tagPipelinesApi = {
  async list(): Promise<TagPipelineDto[]> {
    const { data } = await apiClient.get<TagPipelineDto[]>('/tag-pipelines');
    return data;
  },

  async get(id: string): Promise<TagPipelineDto> {
    const { data } = await apiClient.get<TagPipelineDto>(`/tag-pipelines/${encodeURIComponent(id)}`);
    return data;
  },

  async create(payload: TagPipelineRequestDto): Promise<TagPipelineDto> {
    const { data } = await apiClient.post<TagPipelineDto>('/tag-pipelines', payload);
    return data;
  },

  async update(id: string, payload: TagPipelineRequestDto): Promise<TagPipelineDto> {
    const { data } = await apiClient.put<TagPipelineDto>(`/tag-pipelines/${encodeURIComponent(id)}`, payload);
    return data;
  },

  async remove(id: string): Promise<void> {
    await apiClient.delete(`/tag-pipelines/${encodeURIComponent(id)}`);
  },
};
