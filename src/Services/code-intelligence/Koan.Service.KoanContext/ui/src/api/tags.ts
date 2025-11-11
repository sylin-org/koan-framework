import { apiClient } from './client';
import type { TagVocabularyEntryDto, TagVocabularyRequestDto } from './types';

/**
 * Tag vocabulary management endpoints.
 */
export const tagsApi = {
  async list(): Promise<TagVocabularyEntryDto[]> {
    const { data } = await apiClient.get<TagVocabularyEntryDto[]>('/tags');
    return data;
  },

  async create(payload: TagVocabularyRequestDto): Promise<TagVocabularyEntryDto> {
    const { data } = await apiClient.post<TagVocabularyEntryDto>('/tags', payload);
    return data;
  },

  async update(tag: string, payload: TagVocabularyRequestDto): Promise<TagVocabularyEntryDto> {
    const { data } = await apiClient.put<TagVocabularyEntryDto>(`/tags/${encodeURIComponent(tag)}`, payload);
    return data;
  },

  async remove(tag: string): Promise<void> {
    await apiClient.delete(`/tags/${encodeURIComponent(tag)}`);
  },
};
