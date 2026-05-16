import { apiClient } from './client';
import type { TagRuleDto, TagRuleRequestDto } from './types';

/**
 * Tag rule management endpoints.
 */
export const tagRulesApi = {
  async list(): Promise<TagRuleDto[]> {
    const { data } = await apiClient.get<TagRuleDto[]>('/tag-rules');
    return data;
  },

  async get(id: string): Promise<TagRuleDto> {
    const { data } = await apiClient.get<TagRuleDto>(`/tag-rules/${encodeURIComponent(id)}`);
    return data;
  },

  async create(payload: TagRuleRequestDto): Promise<TagRuleDto> {
    const { data } = await apiClient.post<TagRuleDto>('/tag-rules', payload);
    return data;
  },

  async update(id: string, payload: TagRuleRequestDto): Promise<TagRuleDto> {
    const { data } = await apiClient.put<TagRuleDto>(`/tag-rules/${encodeURIComponent(id)}`, payload);
    return data;
  },

  async remove(id: string): Promise<void> {
    await apiClient.delete(`/tag-rules/${encodeURIComponent(id)}`);
  },
};
