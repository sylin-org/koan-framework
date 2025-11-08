import { apiClient } from './client';
import type { SearchRequest, SearchResult, SuggestionRequest, SuggestionResponse, LanguageStats } from './types';

/**
 * Search API - Semantic search within projects
 */
export const searchApi = {
  /**
   * Perform semantic search within a project or across multiple projects
   */
  async search(request: SearchRequest): Promise<SearchResult> {
    const { data } = await apiClient.post<SearchResult>('/search', request);
    return data;
  },

  /**
   * Get search suggestions based on query prefix
   */
  async getSuggestions(request: SuggestionRequest): Promise<SuggestionResponse> {
    const { data } = await apiClient.post<SuggestionResponse>('/search/suggestions', request);
    return data;
  },

  /**
   * Get available languages/file types for filtering
   */
  async getLanguages(projectId?: string | null, projectIds?: string[] | null): Promise<LanguageStats> {
    const params = new URLSearchParams();
    if (projectId) params.set('projectId', projectId);
    if (projectIds && projectIds.length > 0) params.set('projectIds', projectIds.join(','));

    const { data } = await apiClient.get<LanguageStats>(`/search/languages${params.toString() ? '?' + params.toString() : ''}`);
    return data;
  },
};
