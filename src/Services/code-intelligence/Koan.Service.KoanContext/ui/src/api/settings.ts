import { apiClient } from './client';
import type { AppSettings, ConnectionTestResult } from './types';

/**
 * Settings API - Application configuration management
 */
export const settingsApi = {
  /**
   * Get all current application settings
   */
  async get(): Promise<AppSettings> {
    const { data } = await apiClient.get<AppSettings>('/settings');
    return data;
  },

  /**
   * Test vector store connection
   */
  async testVectorStore(): Promise<ConnectionTestResult> {
    const { data } = await apiClient.post<ConnectionTestResult>('/settings/test/vector-store');
    return data;
  },

  /**
   * Test database connection
   */
  async testDatabase(): Promise<ConnectionTestResult> {
    const { data } = await apiClient.post<ConnectionTestResult>('/settings/test/database');
    return data;
  },
};
