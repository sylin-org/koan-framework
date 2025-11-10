import { apiClient } from './client';
import type {
  SearchCategory,
  SearchAudience,
  CreateSearchCategoryRequest,
  UpdateSearchCategoryRequest,
  CreateSearchAudienceRequest,
  UpdateSearchAudienceRequest,
} from './types';

/**
 * Search Profiles API - Manage search categories and audiences
 */
export const searchProfilesApi = {
  // ========================================
  // Categories
  // ========================================

  /**
   * List all search categories
   */
  async listCategories(): Promise<SearchCategory[]> {
    const { data } = await apiClient.get<SearchCategory[]>('/searchcategories');
    return data;
  },

  /**
   * Get a single category by ID
   */
  async getCategory(id: string): Promise<SearchCategory> {
    const { data } = await apiClient.get<SearchCategory>(`/searchcategories/${id}`);
    return data;
  },

  /**
   * Create a new category
   */
  async createCategory(request: CreateSearchCategoryRequest): Promise<SearchCategory> {
    const { data } = await apiClient.post<SearchCategory>('/searchcategories', request);
    return data;
  },

  /**
   * Update an existing category (PATCH)
   */
  async updateCategory(id: string, updates: UpdateSearchCategoryRequest): Promise<SearchCategory> {
    const { data } = await apiClient.patch<SearchCategory>(`/searchcategories/${id}`, updates);
    return data;
  },

  /**
   * Delete a category
   */
  async deleteCategory(id: string): Promise<void> {
    await apiClient.delete(`/searchcategories/${id}`);
  },

  /**
   * Toggle category active status
   */
  async toggleCategoryActive(id: string, isActive: boolean): Promise<SearchCategory> {
    return this.updateCategory(id, { isActive });
  },

  // ========================================
  // Audiences
  // ========================================

  /**
   * List all search audiences
   */
  async listAudiences(): Promise<SearchAudience[]> {
    const { data } = await apiClient.get<SearchAudience[]>('/searchaudiences');
    return data;
  },

  /**
   * Get a single audience by ID
   */
  async getAudience(id: string): Promise<SearchAudience> {
    const { data } = await apiClient.get<SearchAudience>(`/searchaudiences/${id}`);
    return data;
  },

  /**
   * Create a new audience
   */
  async createAudience(request: CreateSearchAudienceRequest): Promise<SearchAudience> {
    const { data } = await apiClient.post<SearchAudience>('/searchaudiences', request);
    return data;
  },

  /**
   * Update an existing audience (PATCH)
   */
  async updateAudience(id: string, updates: UpdateSearchAudienceRequest): Promise<SearchAudience> {
    const { data } = await apiClient.patch<SearchAudience>(`/searchaudiences/${id}`, updates);
    return data;
  },

  /**
   * Delete an audience
   */
  async deleteAudience(id: string): Promise<void> {
    await apiClient.delete(`/searchaudiences/${id}`);
  },

  /**
   * Toggle audience active status
   */
  async toggleAudienceActive(id: string, isActive: boolean): Promise<SearchAudience> {
    return this.updateAudience(id, { isActive });
  },
};
