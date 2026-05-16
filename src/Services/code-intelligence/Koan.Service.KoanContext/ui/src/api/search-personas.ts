import { apiClient } from './client';
import type { SearchPersonaDto, SearchPersonaRequestDto } from './types';

/**
 * Search persona management endpoints.
 */
export const searchPersonasApi = {
  async list(): Promise<SearchPersonaDto[]> {
    const { data } = await apiClient.get<SearchPersonaDto[]>('/search-personas');
    return data;
  },

  async get(id: string): Promise<SearchPersonaDto> {
    const { data } = await apiClient.get<SearchPersonaDto>(`/search-personas/${encodeURIComponent(id)}`);
    return data;
  },

  async create(payload: SearchPersonaRequestDto): Promise<SearchPersonaDto> {
    const { data } = await apiClient.post<SearchPersonaDto>('/search-personas', payload);
    return data;
  },

  async update(id: string, payload: SearchPersonaRequestDto): Promise<SearchPersonaDto> {
    const { data } = await apiClient.put<SearchPersonaDto>(`/search-personas/${encodeURIComponent(id)}`, payload);
    return data;
  },

  async remove(id: string): Promise<void> {
    await apiClient.delete(`/search-personas/${encodeURIComponent(id)}`);
  },
};
